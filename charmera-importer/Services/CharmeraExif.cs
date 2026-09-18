using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace charmera_importer.Services;

// The facts the importer needs from a Charmera JPEG, read with our own bounds-checked parser
// (general-purpose EXIF readers choke on the camera's corrupt MakerNote offsets).
public sealed record CharmeraExifInfo(
    int Width,
    int Height,
    string? Make,
    string? Model,
    int Orientation,
    DateTime? DateTaken,
    bool HadExif);

public sealed record CharmeraFixResult(
    int Width,
    int Height,
    string Make,
    string Model,
    int Orientation,
    DateTime DateTaken,
    bool DateFromExif);

// Repairs the broken EXIF written by the Kodak Charmera (Generalplus chipset):
//   - dates stored as "YYYY:MM:DD:HH:MM:SS" instead of "YYYY:MM:DD HH:MM:SS", which other
//     software rejects, so photos land on the wrong day or have no date at all;
//   - ExifImageWidth/Height claiming 640x480 for a 1440x1080 image;
//   - a MakerNote with bad IFD offsets that makes editors refuse to touch the file;
//   - no camera Make/Model.
// Fix() writes a fresh, minimal EXIF block with the corrected values and keeps every other
// JPEG segment, including the compressed image data, byte-for-byte. Known issues documented
// by https://github.com/jphastings/charmera and https://github.com/RAIT-09/kodak-charmera-exif-fixer
// (both MIT); this is an independent C# implementation of the same approach.
public static class CharmeraExif
{
    public const string DefaultMake = "Kodak";
    public const string DefaultModel = "Charmera";

    // Published specs (Kodak/Wikipedia): fixed 35 mm-equivalent f/2.4 lens. The real focal
    // length isn't published, so only the 35 mm equivalent is written.
    private const string LensModel = "Charmera fixed lens (35mm-equiv f/2.4)";
    private const uint FNumberNumerator = 24;
    private const uint FNumberDenominator = 10;
    private const ushort FocalLengthIn35mm = 35;

    // Generalplus encoders leave this JPEG comment; it identifies Charmera files regardless
    // of what the memory card or files have been renamed to.
    public const int SignatureSampleSize = 4096;
    private static readonly byte[] Signature = "GPEncoder"u8.ToArray();
    private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();

    private const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";
    private static readonly Regex ValidDate = new(@"^\d{4}:\d{2}:\d{2} \d{2}:\d{2}:\d{2}$", RegexOptions.CultureInvariant);
    private static readonly Regex MalformedDate = new(@"^(\d{4}):(\d{2}):(\d{2}):(\d{2}):(\d{2}):(\d{2})$", RegexOptions.CultureInvariant);

    private const ushort TagMake = 0x010F;
    private const ushort TagModel = 0x0110;
    private const ushort TagOrientation = 0x0112;
    private const ushort TagDateTime = 0x0132;
    private const ushort TagExifIfdPointer = 0x8769;
    private const ushort TagFNumber = 0x829D;
    private const ushort TagExifVersion = 0x9000;
    private const ushort TagDateTimeOriginal = 0x9003;
    private const ushort TagDateTimeDigitized = 0x9004;
    private const ushort TagPixelXDimension = 0xA002;
    private const ushort TagPixelYDimension = 0xA003;
    private const ushort TagFocalLengthIn35mm = 0xA405;
    private const ushort TagLensMake = 0xA433;
    private const ushort TagLensModel = 0xA434;

    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;
    private const ushort TypeUndefined = 7;

    public static bool HasCharmeraSignature(ReadOnlySpan<byte> jpegHeader) =>
        jpegHeader.Length >= 2 && jpegHeader[0] == 0xFF && jpegHeader[1] == 0xD8
        && jpegHeader.IndexOf(Signature) >= 0;

    public static bool IsCharmeraFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[SignatureSampleSize];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return HasCharmeraSignature(buffer.AsSpan(0, read));
        }
        catch
        {
            return false;
        }
    }

    // Returns a standard "YYYY:MM:DD HH:MM:SS" date, repairing the Charmera's all-colons form;
    // null for empty, zeroed or unrecognizable values.
    public static string? NormalizeExifDate(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var trimmed = raw.Trim('\0', ' ');
        if (trimmed.Length == 0 || trimmed.StartsWith("0000", StringComparison.Ordinal))
        {
            return null;
        }

        if (ValidDate.IsMatch(trimmed))
        {
            return trimmed;
        }

        var match = MalformedDate.Match(trimmed);
        return match.Success
            ? $"{match.Groups[1]}:{match.Groups[2]}:{match.Groups[3]} {match.Groups[4]}:{match.Groups[5]}:{match.Groups[6]}"
            : null;
    }

    // The camera's clock has no time zone: the value is local time, and is kept as such.
    public static DateTime? ParseExifDate(string? raw) =>
        NormalizeExifDate(raw) is { } normalized
        && DateTime.TryParseExact(normalized, ExifDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    public static CharmeraExifInfo Read(byte[] jpeg)
    {
        var layout = ScanJpeg(jpeg);
        // +10 skips the APP1 marker, segment length and "Exif\0\0" header.
        var exif = layout.ExifStart >= 0 ? ReadTiff(jpeg[(layout.ExifStart + 10)..layout.ExifEnd]) : null;

        return new CharmeraExifInfo(
            layout.Width,
            layout.Height,
            NullIfBlank(exif?.Make),
            NullIfBlank(exif?.Model),
            exif?.Orientation is >= 1 and <= 8 ? exif.Orientation : 1,
            PickDate(exif),
            exif is not null);
    }

    // Returns the repaired JPEG. `fallbackDate` (the file's modification time) is used only
    // when the EXIF has no usable date at all.
    public static byte[] Fix(byte[] jpeg, DateTime fallbackDate, out CharmeraFixResult result)
    {
        var info = Read(jpeg);
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw new InvalidDataException("Could not read the image dimensions from the JPEG frame header.");
        }

        var date = info.DateTaken ?? fallbackDate;
        var make = info.Make ?? DefaultMake;
        var model = info.Model ?? DefaultModel;
        var dateText = date.ToString(ExifDateFormat, CultureInfo.InvariantCulture);

        var ifd0 = new List<IfdEntry>
        {
            IfdEntry.Ascii(TagMake, make),
            IfdEntry.Ascii(TagModel, model),
            IfdEntry.Short(TagOrientation, (ushort)info.Orientation),
            IfdEntry.Ascii(TagDateTime, dateText),
        };
        var exifIfd = new List<IfdEntry>
        {
            IfdEntry.Rational(TagFNumber, FNumberNumerator, FNumberDenominator),
            new(TagExifVersion, TypeUndefined, 4, "0232"u8.ToArray()),
            IfdEntry.Ascii(TagDateTimeOriginal, dateText),
            IfdEntry.Ascii(TagDateTimeDigitized, dateText),
            IfdEntry.Long(TagPixelXDimension, (uint)info.Width),
            IfdEntry.Long(TagPixelYDimension, (uint)info.Height),
            IfdEntry.Short(TagFocalLengthIn35mm, FocalLengthIn35mm),
            IfdEntry.Ascii(TagLensMake, DefaultMake),
            IfdEntry.Ascii(TagLensModel, LensModel),
        };

        var tiff = BuildTiff(ifd0, exifIfd);
        var segmentLength = 2 + ExifHeader.Length + tiff.Length;
        if (segmentLength > 0xFFFF)
        {
            throw new InvalidDataException("Rebuilt EXIF segment is too large.");
        }

        var segment = new byte[2 + segmentLength];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)segmentLength);
        ExifHeader.CopyTo(segment, 4);
        tiff.CopyTo(segment, 4 + ExifHeader.Length);

        var layout = ScanJpeg(jpeg);
        var (cutStart, cutEnd) = layout.ExifStart >= 0
            ? (layout.ExifStart, layout.ExifEnd)
            : (layout.InsertAt, layout.InsertAt);

        var output = new byte[jpeg.Length - (cutEnd - cutStart) + segment.Length];
        jpeg.AsSpan(0, cutStart).CopyTo(output);
        segment.CopyTo(output, cutStart);
        jpeg.AsSpan(cutEnd).CopyTo(output.AsSpan(cutStart + segment.Length));

        result = new CharmeraFixResult(info.Width, info.Height, make, model, info.Orientation, date, info.DateTaken is not null);
        return output;
    }

    private static DateTime? PickDate(TiffInfo? exif) =>
        exif is null ? null
        : ParseExifDate(exif.DateTimeOriginal) ?? ParseExifDate(exif.DateTimeDigitized) ?? ParseExifDate(exif.DateTime);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ---- JPEG segment scan -------------------------------------------------------------

    private sealed record JpegLayout(int Width, int Height, int ExifStart, int ExifEnd, int InsertAt);

    // Walks the marker segments up to Start-Of-Scan. ExifStart/End delimit the whole APP1 Exif
    // segment (marker included); InsertAt is where one goes when there's none: right after
    // SOI, or after a leading JFIF APP0.
    private static JpegLayout ScanJpeg(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidDataException("Not a JPEG file.");
        }

        int width = 0, height = 0, exifStart = -1, exifEnd = -1, insertAt = 2;
        var pos = 2;
        var first = true;
        while (pos + 4 <= jpeg.Length)
        {
            if (jpeg[pos] != 0xFF)
            {
                throw new InvalidDataException($"Malformed JPEG: expected a marker at offset {pos}.");
            }

            while (pos + 1 < jpeg.Length && jpeg[pos + 1] == 0xFF)
            {
                pos++; // fill bytes
            }

            var marker = jpeg[pos + 1];
            if (marker == 0xDA || marker == 0xD9)
            {
                break; // Start-Of-Scan / End-Of-Image: no more metadata segments
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                pos += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(pos + 2));
            var end = pos + 2 + length;
            if (length < 2 || end > jpeg.Length)
            {
                throw new InvalidDataException($"Malformed JPEG: segment at offset {pos} runs past the end of the file.");
            }

            if (marker == 0xE1 && exifStart < 0 && length >= 2 + ExifHeader.Length
                && jpeg.AsSpan(pos + 4, ExifHeader.Length).SequenceEqual(ExifHeader))
            {
                exifStart = pos;
                exifEnd = end;
            }
            else if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && length >= 7)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(pos + 5));
                width = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(pos + 7));
            }
            else if (marker == 0xE0 && first)
            {
                insertAt = end;
            }

            first = false;
            pos = end;
        }

        return new JpegLayout(width, height, exifStart, exifEnd, insertAt);
    }

    // ---- Tolerant TIFF/EXIF reader ------------------------------------------------------

    private sealed class TiffInfo
    {
        public string? Make;
        public string? Model;
        public int Orientation;
        public string? DateTime;
        public string? DateTimeOriginal;
        public string? DateTimeDigitized;
    }

    // Every read is bounds-checked and failures just leave a field empty: the whole point is
    // to salvage what's usable from a structurally broken block.
    private static TiffInfo? ReadTiff(byte[] tiff)
    {
        if (tiff.Length < 8)
        {
            return null;
        }

        bool littleEndian;
        if (tiff[0] == (byte)'I' && tiff[1] == (byte)'I')
        {
            littleEndian = true;
        }
        else if (tiff[0] == (byte)'M' && tiff[1] == (byte)'M')
        {
            littleEndian = false;
        }
        else
        {
            return null;
        }

        var reader = new TiffReader(tiff, littleEndian);
        var info = new TiffInfo();
        var ifd0Offset = (int)reader.U32(4);
        var exifOffset = -1;
        reader.ForEachEntry(ifd0Offset, (tag, type, count, valueOffset) =>
        {
            switch (tag)
            {
                case TagMake: info.Make = reader.Ascii(type, count, valueOffset); break;
                case TagModel: info.Model = reader.Ascii(type, count, valueOffset); break;
                case TagDateTime: info.DateTime = reader.Ascii(type, count, valueOffset); break;
                case TagOrientation when type == TypeShort: info.Orientation = reader.U16(valueOffset); break;
                case TagExifIfdPointer when type == TypeLong: exifOffset = (int)reader.U32(valueOffset); break;
            }
        });

        if (exifOffset > 0)
        {
            reader.ForEachEntry(exifOffset, (tag, type, count, valueOffset) =>
            {
                switch (tag)
                {
                    case TagDateTimeOriginal: info.DateTimeOriginal = reader.Ascii(type, count, valueOffset); break;
                    case TagDateTimeDigitized: info.DateTimeDigitized = reader.Ascii(type, count, valueOffset); break;
                }
            });
        }

        return info;
    }

    private sealed class TiffReader(byte[] data, bool littleEndian)
    {
        public delegate void EntryVisitor(ushort tag, ushort type, uint count, int valueOffset);

        public ushort U16(int offset) =>
            offset < 0 || offset + 2 > data.Length ? (ushort)0
            : littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));

        public uint U32(int offset) =>
            offset < 0 || offset + 4 > data.Length ? 0u
            : littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

        public void ForEachEntry(int ifdOffset, EntryVisitor visit)
        {
            if (ifdOffset < 8 || ifdOffset + 2 > data.Length)
            {
                return;
            }

            var count = U16(ifdOffset);
            for (var i = 0; i < count; i++)
            {
                var entry = ifdOffset + 2 + i * 12;
                if (entry + 12 > data.Length)
                {
                    return;
                }

                // valueOffset points at the value itself: inline in the entry when it fits in
                // 4 bytes, otherwise wherever the entry's offset field says.
                var type = U16(entry + 2);
                var valueCount = U32(entry + 4);
                var size = type switch { TypeShort => 2L, TypeLong => 4L, TypeRational => 8L, _ => 1L } * valueCount;
                var valueOffset = size <= 4 ? entry + 8 : (int)U32(entry + 8);
                visit(U16(entry), type, valueCount, valueOffset);
            }
        }

        public string? Ascii(ushort type, uint count, int offset)
        {
            if (type != TypeAscii || count == 0 || count > 256 || offset < 0 || offset + count > data.Length)
            {
                return null;
            }

            var text = Encoding.ASCII.GetString(data, offset, (int)count);
            var nul = text.IndexOf('\0');
            return nul >= 0 ? text[..nul] : text;
        }
    }

    // ---- Minimal TIFF writer (little-endian) --------------------------------------------

    private sealed record IfdEntry(ushort Tag, ushort Type, uint Count, byte[] Value)
    {
        public static IfdEntry Ascii(ushort tag, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text + "\0");
            return new IfdEntry(tag, TypeAscii, (uint)bytes.Length, bytes);
        }

        public static IfdEntry Short(ushort tag, ushort value)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            return new IfdEntry(tag, TypeShort, 1, bytes);
        }

        public static IfdEntry Long(ushort tag, uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            return new IfdEntry(tag, TypeLong, 1, bytes);
        }

        public static IfdEntry Rational(ushort tag, uint numerator, uint denominator)
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), denominator);
            return new IfdEntry(tag, TypeRational, 1, bytes);
        }
    }

    private static byte[] BuildTiff(List<IfdEntry> ifd0, List<IfdEntry> exifIfd)
    {
        const int ifd0Offset = 8;
        var ifd0WithPointer = new List<IfdEntry>(ifd0) { IfdEntry.Long(TagExifIfdPointer, 0) };
        var exifOffset = ifd0Offset + IfdSize(ifd0WithPointer);
        ifd0WithPointer[^1] = IfdEntry.Long(TagExifIfdPointer, (uint)exifOffset);

        var buffer = new byte[exifOffset + IfdSize(exifIfd)];
        "II"u8.CopyTo(buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), ifd0Offset);
        WriteIfd(buffer, ifd0Offset, ifd0WithPointer);
        WriteIfd(buffer, exifOffset, exifIfd);
        return buffer;
    }

    private static int IfdSize(List<IfdEntry> entries)
    {
        var size = 2 + entries.Count * 12 + 4;
        foreach (var entry in entries)
        {
            if (entry.Value.Length > 4)
            {
                size += (entry.Value.Length + 1) & ~1; // word-aligned out-of-line values
            }
        }

        return size;
    }

    private static void WriteIfd(byte[] buffer, int offset, List<IfdEntry> entries)
    {
        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag)); // TIFF requires ascending tag order
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)entries.Count);
        var dataOffset = offset + 2 + entries.Count * 12 + 4; // next-IFD offset stays 0
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var at = offset + 2 + i * 12;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at), entry.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at + 2), entry.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at + 4), entry.Count);
            if (entry.Value.Length <= 4)
            {
                entry.Value.CopyTo(buffer, at + 8);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at + 8), (uint)dataOffset);
                entry.Value.CopyTo(buffer, dataOffset);
                dataOffset += (entry.Value.Length + 1) & ~1;
            }
        }
    }
}

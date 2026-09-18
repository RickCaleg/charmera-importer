using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace charmera_importer.Services;

// A date stored as text inside the AVI: where it is and how much room it has, so it can be
// rewritten in place without moving anything else in the file.
public sealed record AviDateField(string ChunkId, long Offset, int Size, string Value);

public sealed record CharmeraAviInfo(
    int Width,
    int Height,
    TimeSpan? Duration,
    double? FramesPerSecond,
    string? VideoCodec,
    int? AudioSampleRate,
    int? AudioChannels,
    int? AudioBitsPerSample,
    IReadOnlyList<AviDateField> DateFields,
    long FirstFrameOffset,
    int FirstFrameSize)
{
    // The recording date the file claims, if any field holds a parseable one.
    public DateTime? EmbeddedDate
    {
        get
        {
            foreach (var dateField in DateFields)
            {
                if (CharmeraAvi.ParseAviDate(dateField.Value) is { } date)
                {
                    return date;
                }
            }

            return null;
        }
    }
}

// Reads the Charmera's videos: Motion-JPEG + PCM audio in an AVI (RIFF) container. The camera
// stamps every video with the same wrong recording date, 2010-06-29 (documented by
// https://github.com/jphastings/charmera and https://github.com/RAIT-09/kodak-charmera-exif-fixer).
// The file's own timestamp, written by the camera to the card, is the real recording time.
//
// Only headers are read (with seeks), never the whole video. Each Motion-JPEG frame is a
// complete JPEG, so the first one doubles as the thumbnail without any video decoder.
public static class CharmeraAvi
{
    // The hard-coded date the Charmera writes into every video.
    public static readonly DateOnly BogusRecordingDate = new(2010, 6, 29);

    private const int MaxDateFieldSize = 64;

    public static bool IsAviName(string path) =>
        path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase);

    public static bool HasAviHeader(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("AVI "u8);

    public static bool IsAviFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            return stream.ReadAtLeast(header, 12, throwOnEndOfStream: false) == 12 && HasAviHeader(header);
        }
        catch
        {
            return false;
        }
    }

    public static CharmeraAviInfo Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static CharmeraAviInfo Read(Stream stream)
    {
        var header = new byte[12];
        if (stream.ReadAtLeast(header, 12, throwOnEndOfStream: false) != 12 || !HasAviHeader(header))
        {
            throw new InvalidDataException("Not an AVI file.");
        }

        var state = new ReadState();
        var riffEnd = Math.Min(stream.Length, 8L + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)));
        WalkChunks(stream, 12, riffEnd, state, depth: 0);

        TimeSpan? duration = state.MicroSecondsPerFrame > 0 && state.TotalFrames > 0
            ? TimeSpan.FromMicroseconds((double)state.MicroSecondsPerFrame * state.TotalFrames)
            : null;
        double? fps = state.MicroSecondsPerFrame > 0 ? 1_000_000.0 / state.MicroSecondsPerFrame : null;

        return new CharmeraAviInfo(
            state.Width,
            state.Height,
            duration,
            fps,
            state.VideoCodec,
            state.AudioSampleRate,
            state.AudioChannels,
            state.AudioBitsPerSample,
            state.DateFields,
            state.FirstFrameOffset,
            state.FirstFrameSize);
    }

    public static byte[]? ReadFirstFrame(string path, CharmeraAviInfo info)
    {
        if (info.FirstFrameOffset <= 0 || info.FirstFrameSize <= 2)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        stream.Seek(info.FirstFrameOffset, SeekOrigin.Begin);
        var frame = new byte[info.FirstFrameSize];
        stream.ReadExactly(frame);
        return frame[0] == 0xFF && frame[1] == 0xD8 ? frame : null; // must be a JPEG
    }

    // Accepts the forms AVI writers use for IDIT/ICRD: C asctime ("Tue Jun 29 12:00:00 2010"),
    // EXIF-style "2010:06:29 12:00:00", and ISO dates.
    public static DateTime? ParseAviDate(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var text = Regex.Replace(raw.Trim('\0', ' ', '\r', '\n'), @"\s+", " ");

        // The weekday in asctime form is redundant, and a writer that gets it wrong would
        // otherwise make the whole date unparseable, so it's ignored.
        text = Regex.Replace(text, @"^[A-Za-z]{3} (?=[A-Za-z]{3} )", string.Empty);
        string[] formats =
        [
            "MMM d HH:mm:ss yyyy",
            "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd", "yyyy/MM/dd",
        ];
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date
            : null;
    }

    // True when the file carries the Charmera's hard-coded fake date.
    public static bool HasBogusDate(CharmeraAviInfo info) =>
        info.DateFields.Any(f => ParseAviDate(f.Value) is { } d && DateOnly.FromDateTime(d) == BogusRecordingDate);

    // Rewrites the bogus date fields of an AVI (a copy, never the camera's file) in place:
    // same format, same byte length, NUL-padded, so no other byte of the file moves. Fields
    // whose new text wouldn't fit are left as they are. Returns how many fields were fixed.
    public static int PatchDates(string path, CharmeraAviInfo info, DateTime recorded)
    {
        var patched = 0;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        foreach (var field in info.DateFields)
        {
            if (ParseAviDate(field.Value) is not { } current || DateOnly.FromDateTime(current) != BogusRecordingDate)
            {
                continue;
            }

            var replacement = Encoding.ASCII.GetBytes(FormatLike(field.Value, recorded));
            if (replacement.Length > field.Size)
            {
                continue;
            }

            var bytes = new byte[field.Size]; // NUL padding past the text
            replacement.CopyTo(bytes, 0);
            stream.Seek(field.Offset, SeekOrigin.Begin);
            stream.Write(bytes);
            patched++;
        }

        return patched;
    }

    // Keeps the original field's style (asctime vs EXIF vs ISO, trailing newline).
    private static string FormatLike(string original, DateTime date)
    {
        var text = original.TrimEnd('\0');
        var newline = text.EndsWith('\n') ? "\n" : string.Empty;
        var core = text.Trim();
        string formatted;
        if (Regex.IsMatch(core, @"^[A-Za-z]{3} "))
        {
            // asctime pads single-digit days with a space ("Jun  9").
            formatted = date.ToString("ddd MMM ", CultureInfo.InvariantCulture)
                + date.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2)
                + date.ToString(" HH:mm:ss yyyy", CultureInfo.InvariantCulture);
        }
        else if (Regex.IsMatch(core, @"^\d{4}:\d{2}:\d{2}"))
        {
            formatted = date.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        else if (core.Length <= 10)
        {
            formatted = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else
        {
            formatted = date.ToString(core.Contains('T') ? "yyyy-MM-ddTHH:mm:ss" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return formatted + newline;
    }

    // ---- RIFF walking ----------------------------------------------------------------

    private sealed class ReadState
    {
        public int Width;
        public int Height;
        public uint MicroSecondsPerFrame;
        public uint TotalFrames;
        public string? VideoCodec;
        public int? AudioSampleRate;
        public int? AudioChannels;
        public int? AudioBitsPerSample;
        public string? CurrentStreamType;
        public readonly List<AviDateField> DateFields = [];
        public long FirstFrameOffset;
        public int FirstFrameSize;
    }

    private static void WalkChunks(Stream stream, long start, long end, ReadState state, int depth)
    {
        var header = new byte[12];
        var position = start;
        while (position + 8 <= end)
        {
            stream.Seek(position, SeekOrigin.Begin);
            if (stream.ReadAtLeast(header.AsSpan(0, 8), 8, throwOnEndOfStream: false) < 8)
            {
                return;
            }

            var id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            var dataStart = position + 8;
            var dataEnd = Math.Min(end, dataStart + size);

            if (id is "LIST" or "RIFF" && size >= 4 && depth < 4)
            {
                stream.ReadExactly(header.AsSpan(8, 4));
                var listType = Encoding.ASCII.GetString(header, 8, 4);
                if (listType == "movi")
                {
                    FindFirstVideoFrame(stream, dataStart + 4, dataEnd, state);
                }
                else
                {
                    WalkChunks(stream, dataStart + 4, dataEnd, state, depth + 1);
                }
            }
            else
            {
                ReadLeafChunk(stream, id, dataStart, (int)Math.Min(size, int.MaxValue), state);
            }

            position = dataStart + size + (size & 1); // chunks are word-aligned
        }
    }

    private static void ReadLeafChunk(Stream stream, string id, long dataStart, int size, ReadState state)
    {
        switch (id)
        {
            case "avih" when size >= 40:
            {
                var avih = ReadBytes(stream, dataStart, 40);
                state.MicroSecondsPerFrame = BinaryPrimitives.ReadUInt32LittleEndian(avih);
                state.TotalFrames = BinaryPrimitives.ReadUInt32LittleEndian(avih.AsSpan(16));
                state.Width = (int)BinaryPrimitives.ReadUInt32LittleEndian(avih.AsSpan(32));
                state.Height = (int)BinaryPrimitives.ReadUInt32LittleEndian(avih.AsSpan(36));
                break;
            }
            case "strh" when size >= 8:
            {
                var strh = ReadBytes(stream, dataStart, 8);
                state.CurrentStreamType = Encoding.ASCII.GetString(strh, 0, 4);
                if (state.CurrentStreamType == "vids")
                {
                    state.VideoCodec ??= Encoding.ASCII.GetString(strh, 4, 4).Trim('\0', ' ');
                }

                break;
            }
            case "strf" when state.CurrentStreamType == "auds" && size >= 16:
            {
                var format = ReadBytes(stream, dataStart, 16); // WAVEFORMATEX
                state.AudioChannels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2));
                state.AudioSampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4));
                state.AudioBitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14));
                break;
            }
            case "IDIT" or "ICRD" when size is > 0 and <= MaxDateFieldSize:
            {
                var value = Encoding.ASCII.GetString(ReadBytes(stream, dataStart, size));
                state.DateFields.Add(new AviDateField(id, dataStart, size, value));
                break;
            }
        }
    }

    private static void FindFirstVideoFrame(Stream stream, long start, long end, ReadState state)
    {
        var header = new byte[8];
        var position = start;
        while (position + 8 <= end)
        {
            stream.Seek(position, SeekOrigin.Begin);
            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
            {
                return;
            }

            var id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            if (id == "LIST")
            {
                // "rec " groups inside movi: step into them.
                position += 12;
                continue;
            }

            // Video chunks are "##dc" (compressed) or "##db"; the first non-empty one wins.
            if (id.Length == 4 && id[2] == 'd' && id[3] is 'c' or 'b' && size > 0)
            {
                state.FirstFrameOffset = position + 8;
                state.FirstFrameSize = (int)Math.Min(size, int.MaxValue);
                return;
            }

            position += 8 + size + (size & 1);
        }
    }

    private static byte[] ReadBytes(Stream stream, long offset, int count)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }
}

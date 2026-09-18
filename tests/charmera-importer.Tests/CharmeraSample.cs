using System.Buffers.Binary;
using System.Text;

namespace charmera_importer.Tests;

// Builds JPEGs whose EXIF matches original Kodak Charmera files byte-structure-wise (checked
// against unedited camera files): little-endian TIFF, Make/Model "Generalplus"/"CBB3" padded
// with spaces, "YYYY:MM:DD:HH:MM:SS" dates, PixelX/YDimension 640x480 on a 1440x1080 frame, a
// MakerNote whose offset points exactly at the end of the EXIF block, and the "GPEncoder"
// JPEG comment.
internal static class CharmeraSample
{
    public const string MalformedDate = "2026:03:03:12:16:29";

    // Stand-in for the entropy-coded image data; only its byte-for-byte survival matters.
    public static readonly byte[] ScanData = Enumerable.Range(0, 64).Select(i => (byte)(i * 7 % 250)).ToArray();

    public static byte[] Build(bool withExif = true, bool bigEndian = false, string date = MalformedDate)
    {
        using var jpeg = new MemoryStream();
        jpeg.Write([0xFF, 0xD8]);

        if (withExif)
        {
            var tiff = BuildBrokenTiff(bigEndian, date);
            WriteSegment(jpeg, 0xE1, [.. "Exif\0\0"u8.ToArray(), .. tiff]);
        }

        WriteSegment(jpeg, 0xFE, "GPEncoder      "u8.ToArray());

        // SOF0: precision 8, height 1080, width 1440, 1 component.
        WriteSegment(jpeg, 0xC0, [8, 0x04, 0x38, 0x05, 0xA0, 1, 1, 0x11, 0]);

        // SOS header + fake scan data + EOI.
        WriteSegment(jpeg, 0xDA, [1, 1, 0, 0, 0x3F, 0]);
        jpeg.Write(ScanData);
        jpeg.Write([0xFF, 0xD9]);
        return jpeg.ToArray();
    }

    // Everything after the SOS marker: the part that must survive a repair unchanged.
    public static byte[] ImageData(byte[] jpeg)
    {
        for (var i = 2; i < jpeg.Length - 1; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xDA)
            {
                return jpeg[i..];
            }
        }

        throw new InvalidOperationException("No SOS marker.");
    }

    private static void WriteSegment(Stream stream, byte marker, byte[] payload)
    {
        stream.Write([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)((payload.Length + 2) & 0xFF)]);
        stream.Write(payload);
    }

    private static byte[] BuildBrokenTiff(bool bigEndian, string date)
    {
        var buffer = new byte[512];
        void U16(int at, int v)
        {
            if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(at), (ushort)v);
            else BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at), (ushort)v);
        }
        void U32(int at, uint v)
        {
            if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(at), v);
            else BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at), v);
        }
        void Entry(int at, int tag, int type, uint count, uint value)
        {
            U16(at, tag);
            U16(at + 2, type);
            U32(at + 4, count);
            if (type == 3 && count == 1)
            {
                U16(at + 8, (int)value); // SHORT values are left-justified in the field
            }
            else
            {
                U32(at + 8, value);
            }
        }

        buffer[0] = buffer[1] = (byte)(bigEndian ? 'M' : 'I');
        U16(2, 42);
        U32(4, 8);

        var make = Encoding.ASCII.GetBytes("Generalplus            \0"); // 24 bytes
        var model = Encoding.ASCII.GetBytes("CBB3           \0");         // 16 bytes
        var dateBytes = Encoding.ASCII.GetBytes(date + "\0");
        const int exifIfd = 62, makeOffset = 140, modelOffset = 164, dateOffset = 180;
        var end = dateOffset + dateBytes.Length;

        // IFD0 @8: Make, Model, DateTime, ExifIFD pointer.
        U16(8, 4);
        Entry(10, 0x010F, 2, (uint)make.Length, makeOffset);
        Entry(22, 0x0110, 2, (uint)model.Length, modelOffset);
        Entry(34, 0x0132, 2, (uint)dateBytes.Length, dateOffset);
        Entry(46, 0x8769, 4, 1, exifIfd);

        // Exif IFD: DateTimeOriginal, DateTimeDigitized, MakerNote (offset = end of block), 640x480.
        U16(exifIfd, 5);
        Entry(exifIfd + 2, 0x9003, 2, (uint)dateBytes.Length, dateOffset);
        Entry(exifIfd + 14, 0x9004, 2, (uint)dateBytes.Length, dateOffset);
        Entry(exifIfd + 26, 0x927C, 7, 1164, (uint)end);
        Entry(exifIfd + 38, 0xA002, 3, 1, 640);
        Entry(exifIfd + 50, 0xA003, 3, 1, 480);

        make.CopyTo(buffer, makeOffset);
        model.CopyTo(buffer, modelOffset);
        dateBytes.CopyTo(buffer, dateOffset);
        return buffer[..end];
    }
}

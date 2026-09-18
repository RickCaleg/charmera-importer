using System.Buffers.Binary;
using System.Text;

namespace charmera_importer.Tests;

// Builds a small AVI shaped like the Charmera's videos: Motion-JPEG video (1440x1080) + PCM
// audio, with the camera's hard-coded fake recording date in the standard date fields
// (hdrl/IDIT in asctime form, INFO/ICRD as an ISO date).
internal static class CharmeraAviSample
{
    public const string BogusIdit = "Tue Jun 29 12:00:00 2010\n";
    public const string BogusIcrd = "2010-06-29";
    public const uint MicroSecondsPerFrame = 33_333;
    public const uint TotalFrames = 90; // 3 s at 30 fps

    public static byte[] Frame => CharmeraSample.Build();

    public static byte[] Build(string? idit = BogusIdit, string? icrd = BogusIcrd)
    {
        var avih = new byte[56];
        W32(avih, 0, MicroSecondsPerFrame);
        W32(avih, 16, TotalFrames);
        W32(avih, 24, 2); // streams
        W32(avih, 32, 1440);
        W32(avih, 36, 1080);

        var videoStrh = new byte[56];
        "vids"u8.CopyTo(videoStrh);
        "MJPG"u8.CopyTo(videoStrh.AsSpan(4));
        var videoStrf = new byte[40]; // BITMAPINFOHEADER
        W32(videoStrf, 0, 40);
        W32(videoStrf, 4, 1440);
        W32(videoStrf, 8, 1080);

        var audioStrh = new byte[56];
        "auds"u8.CopyTo(audioStrh);
        var audioStrf = new byte[16]; // WAVEFORMATEX: PCM, mono, 22050 Hz, 16 bit
        W16(audioStrf, 0, 1);
        W16(audioStrf, 2, 1);
        W32(audioStrf, 4, 22050);
        W32(audioStrf, 8, 44100);
        W16(audioStrf, 12, 2);
        W16(audioStrf, 14, 16);

        var hdrlChildren = new List<byte[]>
        {
            Chunk("avih", avih),
            List("strl", Chunk("strh", videoStrh), Chunk("strf", videoStrf)),
            List("strl", Chunk("strh", audioStrh), Chunk("strf", audioStrf)),
        };
        if (idit is not null)
        {
            hdrlChildren.Add(Chunk("IDIT", Encoding.ASCII.GetBytes(idit + "\0")));
        }

        var top = new List<byte[]> { List("hdrl", [.. hdrlChildren]) };
        if (icrd is not null)
        {
            top.Add(List("INFO", Chunk("ICRD", Encoding.ASCII.GetBytes(icrd + "\0"))));
        }

        top.Add(List("movi", Chunk("01wb", new byte[64]), Chunk("00dc", Frame), Chunk("00dc", Frame)));
        top.Add(Chunk("idx1", new byte[16]));

        var body = Concat(top);
        return Concat([Encoding.ASCII.GetBytes("RIFF"), U32(4 + body.Length), "AVI "u8.ToArray(), body]);
    }

    private static byte[] Chunk(string id, byte[] data)
    {
        var padded = data.Length % 2 == 1 ? [.. data, 0] : data;
        return Concat([Encoding.ASCII.GetBytes(id), U32(data.Length), padded]);
    }

    private static byte[] List(string type, params byte[][] children)
    {
        var body = Concat([Encoding.ASCII.GetBytes(type), .. children]);
        return Concat([Encoding.ASCII.GetBytes("LIST"), U32(body.Length), body]);
    }

    private static byte[] Concat(IEnumerable<byte[]> parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] U32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static void W32(byte[] buffer, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at), value);

    private static void W16(byte[] buffer, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at), value);
}

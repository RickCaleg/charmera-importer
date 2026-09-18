using charmera_importer.Services;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;

namespace charmera_importer.Tests;

public class CharmeraExifTests
{
    private static readonly DateTime Fallback = new(2026, 1, 2, 3, 4, 5);

    private static IReadOnlyList<MetadataExtractor.Directory> ReadWithMetadataExtractor(byte[] jpeg) =>
        ImageMetadataReader.ReadMetadata(new MemoryStream(jpeg));

    [Theory]
    [InlineData("2026:03:03:12:16:29", "2026:03:03 12:16:29")] // the Charmera's form
    [InlineData("2026:03:03 12:16:29", "2026:03:03 12:16:29")] // already valid
    [InlineData("  2026:03:03 12:16:29\0", "2026:03:03 12:16:29")]
    [InlineData("0000:00:00 00:00:00", null)]
    [InlineData("", null)]
    [InlineData("yesterday", null)]
    public void NormalizeExifDate_RepairsCharmeraForm(string raw, string? expected)
    {
        Assert.Equal(expected, CharmeraExif.NormalizeExifDate(raw));
    }

    [Fact]
    public void Sample_ReproducesTheCharmeraDefects()
    {
        // Guards the premise: a standard reader can't use this file's date or dimensions.
        var directories = ReadWithMetadataExtractor(CharmeraSample.Build());
        var subIfd = directories.OfType<ExifSubIfdDirectory>().First();
        var ifd0 = directories.OfType<ExifIfd0Directory>().First();
        Assert.Equal("Generalplus", ifd0.GetString(ExifDirectoryBase.TagMake)?.Trim());

        Assert.False(subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out _));
        Assert.Equal(640, subIfd.GetInt32(ExifDirectoryBase.TagExifImageWidth));
        Assert.Equal(1440, directories.OfType<JpegDirectory>().First().GetImageWidth());
    }

    [Fact]
    public void HasCharmeraSignature_DetectsGeneralplusComment()
    {
        Assert.True(CharmeraExif.HasCharmeraSignature(CharmeraSample.Build()));
        Assert.False(CharmeraExif.HasCharmeraSignature("not a jpeg GPEncoder"u8));
        Assert.False(CharmeraExif.HasCharmeraSignature(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_SalvagesDateAndTrueDimensions(bool bigEndian)
    {
        var info = CharmeraExif.Read(CharmeraSample.Build(bigEndian: bigEndian));

        Assert.Equal(new DateTime(2026, 3, 3, 12, 16, 29), info.DateTaken);
        Assert.Equal((1440, 1080), (info.Width, info.Height));
        Assert.True(info.HadExif);
    }

    [Fact]
    public void Fix_WritesCleanStandardExif()
    {
        var repaired = CharmeraExif.Fix(CharmeraSample.Build(), Fallback, out var result);

        var directories = ReadWithMetadataExtractor(repaired);
        Assert.DoesNotContain(directories, d => d.HasError);
        var ifd0 = directories.OfType<ExifIfd0Directory>().Single();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().Single();

        Assert.Equal("Kodak", ifd0.GetString(ExifDirectoryBase.TagMake));
        Assert.Equal("Charmera", ifd0.GetString(ExifDirectoryBase.TagModel));
        Assert.True(subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken));
        Assert.Equal(new DateTime(2026, 3, 3, 12, 16, 29), taken);
        Assert.Equal(1440, subIfd.GetInt32(ExifDirectoryBase.TagExifImageWidth));
        Assert.Equal(1080, subIfd.GetInt32(ExifDirectoryBase.TagExifImageHeight));
        Assert.False(subIfd.ContainsTag(ExifDirectoryBase.TagMakernote));
        Assert.True(result.DateFromExif);
    }

    [Fact]
    public void Fix_PreservesImageDataAndOtherSegments()
    {
        var original = CharmeraSample.Build();

        var repaired = CharmeraExif.Fix(original, Fallback, out _);

        Assert.Equal(CharmeraSample.ImageData(original), CharmeraSample.ImageData(repaired));
        Assert.True(CharmeraExif.HasCharmeraSignature(repaired)); // GPEncoder comment kept
    }

    [Fact]
    public void Fix_WithoutExif_InsertsOneUsingFallbackDate()
    {
        var repaired = CharmeraExif.Fix(CharmeraSample.Build(withExif: false), Fallback, out var result);

        var subIfd = ReadWithMetadataExtractor(repaired).OfType<ExifSubIfdDirectory>().Single();
        Assert.True(subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken));
        Assert.Equal(Fallback, taken);
        Assert.False(result.DateFromExif);
    }

    [Fact]
    public void Fix_IsIdempotent()
    {
        var once = CharmeraExif.Fix(CharmeraSample.Build(), Fallback, out _);
        var twice = CharmeraExif.Fix(once, Fallback, out _);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Fix_RejectsNonJpeg()
    {
        Assert.Throws<InvalidDataException>(() => CharmeraExif.Fix("GIF89a"u8.ToArray(), Fallback, out _));
    }
}

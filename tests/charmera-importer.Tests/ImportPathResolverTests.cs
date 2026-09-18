using System.Globalization;
using charmera_importer.Models;
using charmera_importer.Services;

namespace charmera_importer.Tests;

public class ImportPathResolverTests
{
    private static readonly DateTime Taken = new(2026, 3, 15, 14, 30, 22);

    private static PhotoImportCandidate Candidate(string? cameraModel = "PIXPRO FZ55") => new()
    {
        SourcePath = "/media/camera/DCIM/100KODAK/IMG_0001.JPG",
        FileName = "IMG_0001.JPG",
        FileSizeBytes = 1234,
        Exif = new PhotoExifData(null, cameraModel, Taken, null, null, null, null, null, null, null, null,
            new Dictionary<string, string>()),
    };

    private static ImportSettings Settings(
        FolderOrganizationScheme scheme = FolderOrganizationScheme.YearMonth,
        FileNamingPreset preset = FileNamingPreset.CompactDateTime,
        bool appendOriginal = true) => new()
    {
        DestinationRootPath = "/photos",
        OrganizationScheme = scheme,
        NamingPreset = preset,
        AppendOriginalFileName = appendOriginal,
    };

    [Theory]
    [InlineData(FolderOrganizationScheme.Flat, "/photos")]
    [InlineData(FolderOrganizationScheme.YearMonth, "/photos/2026/03")]
    [InlineData(FolderOrganizationScheme.YearMonthDay, "/photos/2026/03/15")]
    [InlineData(FolderOrganizationScheme.ByCameraModel, "/photos/PIXPRO FZ55")]
    public void ResolveDestinationFolder_FollowsScheme(FolderOrganizationScheme scheme, string expected)
    {
        var folder = ImportPathResolver.ResolveDestinationFolder(Candidate(), Settings(scheme));

        Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), folder);
    }

    [Fact]
    public void ResolveDestinationFolder_FallsBackWhenCameraModelMissing()
    {
        var folder = ImportPathResolver.ResolveDestinationFolder(Candidate(cameraModel: null), Settings(FolderOrganizationScheme.ByCameraModel));

        Assert.Equal(Path.Combine("/photos", "Unknown Camera"), folder);
    }

    [Theory]
    [InlineData(FileNamingPreset.CompactDateTime, true, "20260315_143022_IMG_0001.JPG")]
    [InlineData(FileNamingPreset.CompactDateTime, false, "20260315_143022.JPG")]
    [InlineData(FileNamingPreset.DashedDateTime, false, "2026-03-15_14-30-22.JPG")]
    public void ResolveDestinationFileName_FollowsPreset(FileNamingPreset preset, bool appendOriginal, string expected)
    {
        var fileName = ImportPathResolver.ResolveDestinationFileName(Candidate(), Settings(preset: preset, appendOriginal: appendOriginal));

        Assert.Equal(expected, fileName);
    }

    [Fact]
    public void DateFormatting_IsCultureIndependent()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // Thai uses the Buddhist calendar (year 2569 for 2026) — folder/file names must not.
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            Assert.Equal(Path.Combine("/photos", "2026", "03"), ImportPathResolver.ResolveDestinationFolder(Candidate(), Settings()));
            Assert.Equal("20260315_143022.JPG", ImportPathResolver.ResolveDestinationFileName(Candidate(), Settings(appendOriginal: false)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ResolveNonCollidingPath_AppendsCounterUntilFree()
    {
        var taken = new HashSet<string> { "/photos/a.jpg", "/photos/a_1.jpg" };

        var path = ImportPathResolver.ResolveNonCollidingPath("/photos/a.jpg", taken.Contains);

        Assert.Equal(Path.Combine("/photos", "a_2.jpg"), path);
    }

    [Fact]
    public void ResolveNonCollidingPath_KeepsFreePath()
    {
        Assert.Equal("/photos/a.jpg", ImportPathResolver.ResolveNonCollidingPath("/photos/a.jpg", _ => false));
    }
}

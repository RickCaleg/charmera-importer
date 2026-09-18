using charmera_importer.Models;
using charmera_importer.Services;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace charmera_importer.Tests;

// End-to-end through the real services: scan-time reading (ExifService) and import-time
// repair (ImportService) against Charmera-style files on disk.
public sealed class CharmeraImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "charmera-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string camera;
    private readonly string destination;

    public CharmeraImportTests()
    {
        camera = Path.Combine(root, "camera", "DCIM");
        destination = Path.Combine(root, "photos");
        System.IO.Directory.CreateDirectory(camera);
    }

    public void Dispose() => System.IO.Directory.Delete(root, recursive: true);

    private async Task<PhotoImportCandidate> CandidateAsync(byte[] content, string name = "PICT0001.JPG")
    {
        var path = Path.Combine(camera, name);
        await File.WriteAllBytesAsync(path, content);
        File.SetLastWriteTime(path, new DateTime(2026, 5, 6, 7, 8, 9));
        return new PhotoImportCandidate
        {
            SourcePath = path,
            FileName = name,
            FileSizeBytes = content.Length,
            FileSystemDateModified = File.GetLastWriteTime(path),
            Exif = await new ExifService().ReadAsync(path),
        };
    }

    private ImportService NewImporter(string historyFile) =>
        new(new Sha256HashingService(), new JsonImportHistoryService(historyFile));

    private ImportSettings Settings(bool repair = true) => new()
    {
        DestinationRootPath = destination,
        OrganizationScheme = FolderOrganizationScheme.YearMonth,
        AppendOriginalFileName = true,
        RepairCharmeraMetadata = repair,
    };

    private static Task Import(ImportService importer, PhotoImportCandidate candidate, ImportSettings settings) =>
        importer.ImportAsync([candidate], settings, new Progress<ImportProgress>());

    [Fact]
    public async Task ExifService_ReadsCharmeraFileCorrectly()
    {
        var candidate = await CandidateAsync(CharmeraSample.Build());

        Assert.NotNull(candidate.Exif);
        Assert.True(candidate.Exif.IsCharmera);
        Assert.Equal(new DateTime(2026, 3, 3, 12, 16, 29), candidate.Exif.DateTaken);
        Assert.Equal((1440, 1080), (candidate.Exif.Width, candidate.Exif.Height));
        Assert.Equal("Charmera", candidate.Exif.CameraModel);
    }

    [Fact]
    public async Task Import_RepairsTheCopyAndLeavesTheCameraUntouched()
    {
        var original = CharmeraSample.Build();
        var candidate = await CandidateAsync(original);

        await Import(NewImporter(Path.Combine(root, "history.json")), candidate, Settings());

        Assert.Equal(ImportStatus.Imported, candidate.Status);
        Assert.Equal(original, await File.ReadAllBytesAsync(candidate.SourcePath));

        // Filed by the repaired capture date, not the file date.
        var imported = Path.Combine(destination, "2026", "03", "20260303_121629_PICT0001.JPG");
        Assert.True(File.Exists(imported));
        var subIfd = ImageMetadataReader.ReadMetadata(imported).OfType<ExifSubIfdDirectory>().Single();
        Assert.True(subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken));
        Assert.Equal(new DateTime(2026, 3, 3, 12, 16, 29), taken);
        Assert.Equal(CharmeraSample.ImageData(original), CharmeraSample.ImageData(await File.ReadAllBytesAsync(imported)));
        Assert.Empty(System.IO.Directory.GetFiles(Path.GetDirectoryName(imported)!, "*.charmera-tmp"));
    }

    [Fact]
    public async Task Import_Twice_IsDetectedAsDuplicate()
    {
        var historyFile = Path.Combine(root, "history.json");
        await Import(NewImporter(historyFile), await CandidateAsync(CharmeraSample.Build()), Settings());

        var history = new JsonImportHistoryService(historyFile);
        await history.LoadAsync();
        var again = await CandidateAsync(CharmeraSample.Build());
        await new ImportService(new Sha256HashingService(), history).ImportAsync([again], Settings(), new Progress<ImportProgress>());

        Assert.Equal(ImportStatus.Duplicate, again.Status);
    }

    [Fact]
    public async Task Import_WithLostHistory_RecognisesTheRepairedCopyAtDestination()
    {
        await Import(NewImporter(Path.Combine(root, "history-a.json")), await CandidateAsync(CharmeraSample.Build()), Settings());

        var again = await CandidateAsync(CharmeraSample.Build());
        await Import(NewImporter(Path.Combine(root, "history-b.json")), again, Settings());

        Assert.Equal(ImportStatus.Duplicate, again.Status);
        Assert.Single(System.IO.Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Import_WithRepairDisabled_CopiesVerbatim()
    {
        var original = CharmeraSample.Build();
        var candidate = await CandidateAsync(original);

        await Import(NewImporter(Path.Combine(root, "history.json")), candidate, Settings(repair: false));

        var imported = System.IO.Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Single();
        Assert.Equal(original, await File.ReadAllBytesAsync(imported));
    }
}

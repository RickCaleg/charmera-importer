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

    private ImportSettings Settings() => new()
    {
        DestinationRootPath = destination,
        OrganizationScheme = FolderOrganizationScheme.YearMonth,
        AppendOriginalFileName = true,
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
        Assert.Equal("Kodak", candidate.Exif.CameraMake); // not the file's "Generalplus"
        Assert.Equal("Charmera", candidate.Exif.CameraModel); // not "CBB3"
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
    public async Task Import_UnparseableFile_IsCopiedVerbatimRatherThanLost()
    {
        // Carries the signature but has no frame header, so the repair can't run.
        byte[] broken = [0xFF, 0xD8, 0xFF, 0xFE, 0x00, 0x0B, .. "GPEncoder"u8.ToArray(), 0xFF, 0xD9];
        var candidate = await CandidateAsync(broken);

        await Import(NewImporter(Path.Combine(root, "history.json")), candidate, Settings());

        Assert.Equal(ImportStatus.Imported, candidate.Status);
        var imported = System.IO.Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Single();
        Assert.Equal(broken, await File.ReadAllBytesAsync(imported));
    }

    [Fact]
    public async Task Scanner_ListsOnlyCharmeraPhotos()
    {
        await File.WriteAllBytesAsync(Path.Combine(camera, "PICT0001.JPG"), CharmeraSample.Build());
        await File.WriteAllBytesAsync(Path.Combine(camera, "other-camera.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        await File.WriteAllBytesAsync(Path.Combine(camera, "notes.png"), [0x89, 0x50, 0x4E, 0x47]);

        var found = await new PhotoScannerService().ScanAsync(Path.GetDirectoryName(camera)!);

        Assert.Equal("PICT0001.JPG", Assert.Single(found).FileName);
    }

    [Fact]
    public async Task IsCharmeraVolume_RecognisesTheCardByContentNotName()
    {
        var volume = Path.GetDirectoryName(camera)!;
        Assert.False(CharmeraExif.IsCharmeraVolume(volume)); // DCIM alone isn't enough

        await File.WriteAllBytesAsync(Path.Combine(camera, "IMG_0001.JPG"), [0xFF, 0xD8, 0xFF, 0xD9]);
        Assert.False(CharmeraExif.IsCharmeraVolume(volume)); // another camera's photo

        await File.WriteAllBytesAsync(Path.Combine(camera, "PICT0001.JPG"), CharmeraSample.Build());
        Assert.True(CharmeraExif.IsCharmeraVolume(volume)); // encoder signature

        var cardWithFolder = Path.Combine(root, "card2");
        System.IO.Directory.CreateDirectory(Path.Combine(cardWithFolder, "DCIM"));
        System.IO.Directory.CreateDirectory(Path.Combine(cardWithFolder, "SPIDCIM"));
        Assert.True(CharmeraExif.IsCharmeraVolume(cardWithFolder)); // the camera's own folder, even when empty
    }
}

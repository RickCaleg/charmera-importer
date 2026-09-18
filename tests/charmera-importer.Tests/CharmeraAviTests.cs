using charmera_importer.Models;
using charmera_importer.Services;

namespace charmera_importer.Tests;

public sealed class CharmeraAviTests : IDisposable
{
    private static readonly DateTime Recorded = new(2026, 8, 7, 18, 45, 12);

    private readonly string root = Path.Combine(Path.GetTempPath(), "charmera-avi-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string camera;
    private readonly string destination;

    public CharmeraAviTests()
    {
        camera = Path.Combine(root, "card", "DCIM");
        destination = Path.Combine(root, "photos");
        Directory.CreateDirectory(camera);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string WriteVideo(byte[] content, string name = "PICT0001.AVI")
    {
        var path = Path.Combine(camera, name);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTime(path, Recorded); // the camera's timestamp = real recording time
        return path;
    }

    [Theory]
    [InlineData("Tue Jun 29 12:00:00 2010\n", 2010, 6, 29)]
    [InlineData("Wed Jun  9 12:00:00 2010", 2010, 6, 9)]
    [InlineData("Mon Jun 29 12:00:00 2010", 2010, 6, 29)] // wrong weekday: still read
    [InlineData("2010:06:29 12:00:00", 2010, 6, 29)]
    [InlineData("2010-06-29\0", 2010, 6, 29)]
    public void ParseAviDate_AcceptsCommonForms(string raw, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), DateOnly.FromDateTime(CharmeraAvi.ParseAviDate(raw)!.Value));
    }

    [Fact]
    public void Read_ParsesHeadersDatesAndFirstFrame()
    {
        var path = WriteVideo(CharmeraAviSample.Build());

        var info = CharmeraAvi.Read(path);

        Assert.Equal((1440, 1080), (info.Width, info.Height));
        Assert.Equal(3.0, info.Duration!.Value.TotalSeconds, precision: 1);
        Assert.Equal(30.0, info.FramesPerSecond!.Value, precision: 0);
        Assert.Equal("MJPG", info.VideoCodec);
        Assert.Equal((22050, 1, 16), (info.AudioSampleRate, info.AudioChannels, info.AudioBitsPerSample));
        Assert.Equal(["IDIT", "ICRD"], info.DateFields.Select(f => f.ChunkId));
        Assert.True(CharmeraAvi.HasBogusDate(info));
        Assert.Equal(CharmeraAviSample.Frame, CharmeraAvi.ReadFirstFrame(path, info));
    }

    [Fact]
    public void PatchDates_RewritesOnlyTheDateBytes()
    {
        var original = CharmeraAviSample.Build();
        var path = WriteVideo(original);
        var info = CharmeraAvi.Read(path);

        Assert.Equal(2, CharmeraAvi.PatchDates(path, info, Recorded));

        var patched = File.ReadAllBytes(path);
        Assert.Equal(original.Length, patched.Length);
        var after = CharmeraAvi.Read(path);
        Assert.False(CharmeraAvi.HasBogusDate(after));
        Assert.Equal("Fri Aug  7 18:45:12 2026\n", after.DateFields[0].Value.TrimEnd('\0'));
        Assert.Equal("2026-08-07", after.DateFields[1].Value.TrimEnd('\0'));

        // Nothing outside the two date fields moved.
        var dateBytes = info.DateFields.SelectMany(f => Enumerable.Range((int)f.Offset, f.Size)).ToHashSet();
        var changed = Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]);
        Assert.All(changed, i => Assert.Contains(i, dateBytes));
    }

    [Fact]
    public void PatchDates_LeavesGenuineDatesAlone()
    {
        var original = CharmeraAviSample.Build(idit: "Sat Aug  1 10:00:00 2026\n", icrd: null);
        var path = WriteVideo(original);
        var info = CharmeraAvi.Read(path);

        Assert.False(CharmeraAvi.HasBogusDate(info));
        Assert.Equal(0, CharmeraAvi.PatchDates(path, info, Recorded));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task ExifService_UsesTheFileTimestampInsteadOfTheFakeDate()
    {
        var path = WriteVideo(CharmeraAviSample.Build());

        var data = await new ExifService().ReadAsync(path);

        Assert.NotNull(data);
        Assert.Equal(Recorded, data.DateTaken);
        Assert.Equal((1440, 1080), (data.Width, data.Height));
        Assert.Equal(3.0, data.Duration!.Value.TotalSeconds, precision: 1);
        Assert.Equal(("Kodak", "Charmera"), (data.CameraMake, data.CameraModel));
    }

    [Fact]
    public async Task Scanner_ListsCharmeraVideosAlongsidePhotos()
    {
        WriteVideo(CharmeraAviSample.Build());
        await File.WriteAllBytesAsync(Path.Combine(camera, "PICT0002.JPG"), CharmeraSample.Build());
        await File.WriteAllBytesAsync(Path.Combine(camera, "fake.avi"), "not a video"u8.ToArray());

        var found = await new PhotoScannerService().ScanAsync(Path.GetDirectoryName(camera)!);

        Assert.Equal(2, found.Count);
        Assert.True(found.Single(c => c.FileName == "PICT0001.AVI").IsVideo);
        Assert.False(found.Single(c => c.FileName == "PICT0002.JPG").IsVideo);
    }

    private async Task<PhotoImportCandidate> CandidateAsync(string path)
    {
        var info = new FileInfo(path);
        return new PhotoImportCandidate
        {
            SourcePath = path,
            FileName = info.Name,
            FileSizeBytes = info.Length,
            FileSystemDateModified = info.LastWriteTime,
            IsVideo = true,
            Exif = await new ExifService().ReadAsync(path),
        };
    }

    private static ImportSettings Settings(string destination) => new()
    {
        DestinationRootPath = destination,
        OrganizationScheme = FolderOrganizationScheme.YearMonth,
        AppendOriginalFileName = true,
    };

    private static Task Import(string historyFile, PhotoImportCandidate candidate, ImportSettings settings) =>
        new ImportService(new Sha256HashingService(), new JsonImportHistoryService(historyFile))
            .ImportAsync([candidate], settings, new Progress<ImportProgress>());

    [Fact]
    public async Task Import_FilesByRealDateAndRepairsTheCopy()
    {
        var original = CharmeraAviSample.Build();
        var candidate = await CandidateAsync(WriteVideo(original));

        await Import(Path.Combine(root, "h.json"), candidate, Settings(destination));

        Assert.Equal(ImportStatus.Imported, candidate.Status);
        Assert.Equal(original, File.ReadAllBytes(candidate.SourcePath)); // camera untouched
        var imported = Path.Combine(destination, "2026", "08", "20260807_184512_PICT0001.AVI");
        Assert.True(File.Exists(imported));
        Assert.False(CharmeraAvi.HasBogusDate(CharmeraAvi.Read(imported)));
        Assert.Equal(original.Length, new FileInfo(imported).Length);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(imported)!, "*.charmera-tmp"));
    }

    [Fact]
    public async Task Import_WithLostHistory_RecognisesTheRepairedVideoAtDestination()
    {
        var path = WriteVideo(CharmeraAviSample.Build());
        await Import(Path.Combine(root, "a.json"), await CandidateAsync(path), Settings(destination));

        var again = await CandidateAsync(path);
        await Import(Path.Combine(root, "b.json"), again, Settings(destination));

        Assert.Equal(ImportStatus.Duplicate, again.Status);
        Assert.Single(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
    }
}

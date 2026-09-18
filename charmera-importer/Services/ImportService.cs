using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using charmera_importer.Localization;
using charmera_importer.Models;

namespace charmera_importer.Services;

public sealed class ImportService : IImportService
{
    private readonly IHashingService hashingService;
    private readonly IImportHistoryService historyService;

    public ImportService(IHashingService hashingService, IImportHistoryService historyService)
    {
        this.hashingService = hashingService;
        this.historyService = historyService;
    }

    public async Task ImportAsync(
        IReadOnlyList<PhotoImportCandidate> candidates,
        ImportSettings settings,
        IProgress<ImportProgress> progress,
        CancellationToken ct = default)
    {
        var total = candidates.Count;
        var completed = 0;

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(new ImportProgress(completed, total, candidate.FileName));

            try
            {
                await ImportOneAsync(candidate, settings, ct);
            }
            catch (Exception ex)
            {
                candidate.Status = ImportStatus.Error;
                candidate.StatusMessage = LocalizedStrings.Instance.ImportError(ex.Message);
            }

            completed++;
            progress.Report(new ImportProgress(completed, total, candidate.FileName));
        }
    }

    private async Task ImportOneAsync(PhotoImportCandidate candidate, ImportSettings settings, CancellationToken ct)
    {
        candidate.Sha256Hash ??= await hashingService.ComputeSha256Async(candidate.SourcePath, ct);

        if (historyService.ContainsHash(candidate.Sha256Hash))
        {
            candidate.Status = ImportStatus.Duplicate;
            candidate.StatusMessage = LocalizedStrings.Instance.AlreadyImportedMessage;
            TryDeleteSource(candidate, settings);
            return;
        }

        var destinationFolder = ImportPathResolver.ResolveDestinationFolder(candidate, settings);
        var destinationFileName = ImportPathResolver.ResolveDestinationFileName(candidate, settings);
        var desiredPath = Path.Combine(destinationFolder, destinationFileName);

        if (candidate.IsVideo)
        {
            await ImportVideoAsync(candidate, settings, destinationFolder, desiredPath, ct);
            return;
        }

        // Written with repaired EXIF (see CharmeraExif); the camera's file is never modified.
        // Only a file the repair can't parse (null) is copied verbatim.
        var repaired = await TryRepairCharmeraAsync(candidate, ct);
        var expectedHash = repaired is null ? candidate.Sha256Hash : Convert.ToHexStringLower(SHA256.HashData(repaired));
        var expectedSize = repaired?.LongLength ?? candidate.FileSizeBytes;

        if (File.Exists(desiredPath) && await IsSameContentAsync(desiredPath, expectedHash, expectedSize, ct))
        {
            // Target already holds identical content (e.g. the history file was lost or this
            // is the first run against a pre-populated destination) — treat as a duplicate
            // instead of writing a redundant "_1" copy next to it.
            candidate.Status = ImportStatus.Duplicate;
            candidate.StatusMessage = LocalizedStrings.Instance.AlreadyAtDestinationMessage;
            TryDeleteSource(candidate, settings);
            return;
        }

        var destinationPath = ImportPathResolver.ResolveNonCollidingPath(desiredPath, File.Exists);

        Directory.CreateDirectory(destinationFolder);
        if (repaired is null)
        {
            File.Copy(candidate.SourcePath, destinationPath, overwrite: false);
        }
        else
        {
            await WriteRepairedAsync(repaired, destinationPath, candidate, ct);
        }

        await historyService.RecordImportAsync(
            new ImportHistoryEntry(candidate.Sha256Hash, candidate.FileName, destinationPath, DateTime.UtcNow, candidate.FileSizeBytes),
            ct);

        candidate.Status = ImportStatus.Imported;
        candidate.StatusMessage = repaired is null
            ? LocalizedStrings.Instance.ImportedMessage
            : LocalizedStrings.Instance.ImportedRepairedMessage;
        TryDeleteSource(candidate, settings);
    }

    // Videos are too large to stage in memory, so the copy is made to a temporary file next to
    // its destination, repaired there when it carries the Charmera's fake 2010-06-29 date,
    // hashed (to recognise an identical file already at the destination) and then renamed
    // into place. Everything else in the video is copied byte for byte.
    private async Task ImportVideoAsync(
        PhotoImportCandidate candidate, ImportSettings settings, string destinationFolder, string desiredPath, CancellationToken ct)
    {
        Directory.CreateDirectory(destinationFolder);
        var tempPath = desiredPath + ".charmera-tmp";
        try
        {
            File.Copy(candidate.SourcePath, tempPath, overwrite: true);
            var repaired = TryRepairVideoDate(tempPath, candidate);
            if (candidate.FileSystemDateModified is { } modified)
            {
                File.SetLastWriteTime(tempPath, modified);
            }

            var outputHash = await hashingService.ComputeSha256Async(tempPath, ct);
            if (File.Exists(desiredPath) && await IsSameContentAsync(desiredPath, outputHash, new FileInfo(tempPath).Length, ct))
            {
                candidate.Status = ImportStatus.Duplicate;
                candidate.StatusMessage = LocalizedStrings.Instance.AlreadyAtDestinationMessage;
                TryDeleteSource(candidate, settings);
                return;
            }

            var destinationPath = ImportPathResolver.ResolveNonCollidingPath(desiredPath, File.Exists);
            File.Move(tempPath, destinationPath, overwrite: false);

            await historyService.RecordImportAsync(
                new ImportHistoryEntry(candidate.Sha256Hash!, candidate.FileName, destinationPath, DateTime.UtcNow, candidate.FileSizeBytes),
                ct);

            candidate.Status = ImportStatus.Imported;
            candidate.StatusMessage = repaired
                ? LocalizedStrings.Instance.ImportedRepairedMessage
                : LocalizedStrings.Instance.ImportedMessage;
            TryDeleteSource(candidate, settings);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    // Replaces the fake date with the real recording time (the one used to name and file the
    // video). A file that can't be parsed, or has no fake date, keeps its bytes as they are.
    private static bool TryRepairVideoDate(string copyPath, PhotoImportCandidate candidate)
    {
        try
        {
            var info = CharmeraAvi.Read(copyPath);
            return CharmeraAvi.HasBogusDate(info)
                && CharmeraAvi.PatchDates(copyPath, info, candidate.ResolveDate()) > 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException)
        {
            return false;
        }
    }

    private async Task<bool> IsSameContentAsync(string existingPath, string expectedHash, long expectedSize, CancellationToken ct)
    {
        if (new FileInfo(existingPath).Length != expectedSize)
        {
            return false;
        }

        var existingHash = await hashingService.ComputeSha256Async(existingPath, ct);
        return string.Equals(existingHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    // A file the repair can't parse is imported untouched rather than failing: the photo
    // matters more than its metadata.
    private static async Task<byte[]?> TryRepairCharmeraAsync(PhotoImportCandidate candidate, CancellationToken ct)
    {
        try
        {
            var original = await File.ReadAllBytesAsync(candidate.SourcePath, ct);
            return CharmeraExif.Fix(original, candidate.FileSystemDateModified ?? DateTime.Now, out _);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    // Written to a temporary name and renamed into place, so an interrupted import never
    // leaves a truncated photo behind. Keeps the original's modification time, like a copy.
    private static async Task WriteRepairedAsync(byte[] repaired, string destinationPath, PhotoImportCandidate candidate, CancellationToken ct)
    {
        var tempPath = destinationPath + ".charmera-tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, repaired, ct);
            if (candidate.FileSystemDateModified is { } modified)
            {
                File.SetLastWriteTime(tempPath, modified);
            }

            File.Move(tempPath, destinationPath, overwrite: false);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    // Only ever called once a photo is confirmed safely stored (freshly copied, or already
    // present at the destination/in history) — never for ImportStatus.Error. A failure here is
    // appended to the existing status message rather than replacing it, so the successful
    // import/dedup result isn't lost from view.
    private static void TryDeleteSource(PhotoImportCandidate candidate, ImportSettings settings)
    {
        if (!settings.DeleteSourceAfterImport)
        {
            return;
        }

        try
        {
            File.Delete(candidate.SourcePath);
        }
        catch (Exception ex)
        {
            candidate.StatusMessage = $"{candidate.StatusMessage} ({LocalizedStrings.Instance.DeleteFailedNote(ex.Message)})";
        }
    }
}

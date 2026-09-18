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

        // Charmera photos are written with repaired EXIF; everything else is copied verbatim.
        // The camera's original file is never modified either way.
        var repaired = settings.RepairCharmeraMetadata && candidate.Exif?.IsCharmera == true
            ? await TryRepairCharmeraAsync(candidate, ct)
            : null;
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

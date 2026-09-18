using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using charmera_importer.Models;

namespace charmera_importer.Services;

public sealed class PhotoScannerService : IPhotoScannerService
{
    public Task<IReadOnlyList<PhotoImportCandidate>> ScanAsync(string rootPath, CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<PhotoImportCandidate>>(() =>
        {
            var dcimPath = Path.Combine(rootPath, "DCIM");
            var scanRoot = Directory.Exists(dcimPath) ? dcimPath : rootPath;

            var candidates = new List<PhotoImportCandidate>();

            foreach (var filePath in Directory.EnumerateFiles(scanRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                // Charmera photos only (JPEGs carrying its encoder signature): anything else on
                // the card, e.g. files copied onto it from elsewhere, isn't ours to import.
                if (!CharmeraExif.IsJpegName(filePath) || !CharmeraExif.IsCharmeraFile(filePath))
                {
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(filePath);
                }
                catch
                {
                    continue;
                }

                candidates.Add(new PhotoImportCandidate
                {
                    SourcePath = filePath,
                    FileName = info.Name,
                    FileSizeBytes = info.Length,
                    FileSystemDateModified = info.LastWriteTime,
                });
            }

            return candidates;
        }, ct);
    }
}

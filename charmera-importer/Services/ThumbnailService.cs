using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace charmera_importer.Services;

public sealed class ThumbnailService : IThumbnailService
{
    public Task<Bitmap?> CreateThumbnailAsync(string filePath, int maxWidth = 220, CancellationToken ct = default)
    {
        return Task.Run<Bitmap?>(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Charmera videos are Motion-JPEG: their first frame is a JPEG to show as-is.
                if (CharmeraAvi.IsAviName(filePath))
                {
                    var frame = CharmeraAvi.ReadFirstFrame(filePath, CharmeraAvi.Read(filePath));
                    return frame is null ? null : Bitmap.DecodeToWidth(new MemoryStream(frame), maxWidth);
                }

                using var stream = File.OpenRead(filePath);
                return Bitmap.DecodeToWidth(stream, maxWidth);
            }
            catch
            {
                // Corrupt file or undecodable frame: the UI falls back to a placeholder; metadata
                // reading is independent.
                return null;
            }
        }, ct);
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using charmera_importer.Models;

namespace charmera_importer.Services;

// Pure, side-effect-free path resolution logic — kept separate from IImportService so
// naming/organization rules can be sanity-checked without touching the filesystem.
public static class ImportPathResolver
{
    public static string ResolveDestinationFolder(PhotoImportCandidate candidate, ImportSettings settings)
    {
        var date = candidate.ResolveDate();

        return settings.OrganizationScheme switch
        {
            FolderOrganizationScheme.Flat =>
                settings.DestinationRootPath,
            FolderOrganizationScheme.YearMonth =>
                Path.Combine(settings.DestinationRootPath, date.ToString("yyyy", CultureInfo.InvariantCulture), date.ToString("MM", CultureInfo.InvariantCulture)),
            FolderOrganizationScheme.YearMonthDay =>
                Path.Combine(settings.DestinationRootPath, date.ToString("yyyy", CultureInfo.InvariantCulture), date.ToString("MM", CultureInfo.InvariantCulture), date.ToString("dd", CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
    }

    public static string ResolveDestinationFileName(PhotoImportCandidate candidate, ImportSettings settings)
    {
        var date = candidate.ResolveDate();
        var preset = NamingPresetOption.All.First(p => p.Value == settings.NamingPreset);
        var formattedDate = preset.Format(date);
        var extension = Path.GetExtension(candidate.FileName);

        if (settings.AppendOriginalFileName)
        {
            var originalNameWithoutExtension = Path.GetFileNameWithoutExtension(candidate.FileName);
            return $"{formattedDate}_{originalNameWithoutExtension}{extension}";
        }

        return $"{formattedDate}{extension}";
    }

    // Appends a numeric suffix (e.g. "_1") before the extension until the path doesn't collide.
    public static string ResolveNonCollidingPath(string desiredPath, Func<string, bool> pathExists)
    {
        if (!pathExists(desiredPath))
        {
            return desiredPath;
        }

        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);

        var counter = 1;
        string candidatePath;
        do
        {
            candidatePath = Path.Combine(directory, $"{nameWithoutExtension}_{counter}{extension}");
            counter++;
        } while (pathExists(candidatePath));

        return candidatePath;
    }

}

namespace charmera_importer.Models;

// Persisted user preferences (~/.local/share/charmera-importer/settings.json on Linux).
// Deliberately excludes DeleteSourceAfterImport — that checkbox always starts unchecked,
// since remembering "on" across launches would make a destructive action too easy to trigger
// by accident.
public sealed record AppSettings(
    string? LanguageCode,
    string? DestinationRootPath,
    FolderOrganizationScheme? OrganizationScheme,
    FileNamingPreset? NamingPreset,
    bool AppendOriginalFileName = true,
    bool CheckForUpdates = true);

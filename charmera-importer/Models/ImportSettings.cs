namespace charmera_importer.Models;

public sealed class ImportSettings
{
    public required string DestinationRootPath { get; init; }
    public FolderOrganizationScheme OrganizationScheme { get; init; } = FolderOrganizationScheme.YearMonth;
    public FileNamingPreset NamingPreset { get; init; } = FileNamingPreset.CompactDateTime;
    public bool AppendOriginalFileName { get; init; } = true;
    public bool DeleteSourceAfterImport { get; init; }
}

namespace charmera_importer.Models;

// Deliberately mutually exclusive, single-selection options (not combinable, e.g. no
// "by camera model, then by year/month" nesting). Keeps naming/path resolution simple;
// combining schemes was considered and explicitly deferred as a future enhancement.
public enum FolderOrganizationScheme
{
    Flat,
    YearMonth,
    YearMonthDay,
}

using CommunityToolkit.Mvvm.ComponentModel;
using charmera_importer.Localization;

namespace charmera_importer.Models;

// ObservableObject (not a plain record) because DisplayName is derived from the active
// language and must refresh already-bound ComboBox items when the user switches language —
// these instances are static/permanent, so subscribing once here for the app's lifetime is safe.
public sealed class FolderOrganizationOption : ObservableObject
{
    public FolderOrganizationScheme Value { get; }
    public string DisplayName => LocalizedStrings.Instance.OrganizationDisplayName(Value);

    public FolderOrganizationOption(FolderOrganizationScheme value)
    {
        Value = value;
        LocalizedStrings.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DisplayName));
    }

    public static readonly FolderOrganizationOption[] All =
    [
        new(FolderOrganizationScheme.YearMonth),
        new(FolderOrganizationScheme.YearMonthDay),
        new(FolderOrganizationScheme.Flat),
    ];
}

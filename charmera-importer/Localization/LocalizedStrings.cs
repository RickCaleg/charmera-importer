using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using charmera_importer.Models;

namespace charmera_importer.Localization;

// App-wide singleton holding the active language's strings. XAML binds to it via the
// {loc:Loc Key} markup extension (see LocExtension.cs); C# code (ViewModels, Services)
// reads it directly as LocalizedStrings.Instance. Apply() raises OnPropertyChanged(string.Empty),
// which refreshes every bound property at once — the same "all properties changed" convention
// INotifyPropertyChanged consumers (including Avalonia's binding engine) already understand.
public sealed class LocalizedStrings : ObservableObject
{
    public static LocalizedStrings Instance { get; } = new();

    private IReadOnlyDictionary<string, string> map = Translations.Get(LanguageOption.English.Code);

    private LocalizedStrings()
    {
    }

    public void Apply(string languageCode)
    {
        map = Translations.Get(languageCode);
        OnPropertyChanged(string.Empty);
    }

    private string Get(string key) => map.TryGetValue(key, out var value) ? value : key;

    public string DevicePlaceholder => Get("Header_DevicePlaceholder");
    public string RefreshTooltip => Get("Header_RefreshTooltip");

    public string NoDestinationSelected => Get("Sidebar_NoDestinationSelected");
    public string BrowseButton => Get("Sidebar_BrowseButton");
    public string OrganizeLabel => Get("Sidebar_OrganizeLabel");
    public string NamingLabel => Get("Sidebar_NamingLabel");
    public string KeepOriginalName => Get("Sidebar_KeepOriginalName");
    public string DeleteAfterImport => Get("Sidebar_DeleteAfterImport");
    public string DeleteAfterImportWarning => Get("Sidebar_DeleteAfterImportWarning");
    public string ImportButtonDefault => Get("Sidebar_ImportButton");

    public string PhotosTitle => Get("Content_PhotosTitle");

    public string DetailTitle => Get("Detail_Title");
    public string DetailMake => Get("Detail_Make");
    public string DetailModel => Get("Detail_Model");
    public string DetailDate => Get("Detail_Date");
    public string DetailDimensions => Get("Detail_Dimensions");
    public string DetailNoExifNote => Get("Detail_NoExifNote");
    public string DetailAllTags => Get("Detail_AllTags");

    public string OrgYearMonth => Get("Org_YearMonth");
    public string OrgYearMonthDay => Get("Org_YearMonthDay");
    public string OrgByCameraModel => Get("Org_ByCameraModel");
    public string OrgFlat => Get("Org_Flat");

    public string ScanningStatus => Get("Status_Scanning");
    public string ImportCompleteStatus => Get("Status_ImportComplete");

    public string AlreadyImportedMessage => Get("Import_AlreadyImported");
    public string AlreadyAtDestinationMessage => Get("Import_AlreadyAtDestination");
    public string ImportedMessage => Get("Import_Success");

    public string FolderPickerTitle => Get("FolderPicker_Title");

    public string StepSource => Get("Step_Source");
    public string StepDestination => Get("Step_Destination");
    public string StepAfterImport => Get("Step_AfterImport");
    public string SourceNoDevice => Get("Source_NoDevice");
    public string DestChangeButton => Get("Dest_ChangeButton");
    public string DestPreviewLabel => Get("Dest_PreviewLabel");
    public string HintSelectCamera => Get("Hint_SelectCamera");
    public string HintNoPhotos => Get("Hint_NoPhotos");
    public string HintChooseDestination => Get("Hint_ChooseDestination");
    public string EmptyNoDeviceTitle => Get("Empty_NoDeviceTitle");
    public string EmptyNoDeviceSubtitle => Get("Empty_NoDeviceSubtitle");
    public string EmptyNoPhotosTitle => Get("Empty_NoPhotosTitle");
    public string EmptyNoPhotosSubtitle => Get("Empty_NoPhotosSubtitle");
    public string SettingsTitle => Get("Settings_Title");
    public string SettingsLanguage => Get("Settings_Language");
    public string SettingsUpdates => Get("Settings_Updates");
    public string DetailCloseTooltip => Get("Detail_CloseTooltip");

    public string UpdateInstallButton => Get("Update_InstallButton");
    public string UpdateDownloadButton => Get("Update_DownloadButton");
    public string UpdateReleaseNotesButton => Get("Update_ReleaseNotesButton");
    public string UpdateDismissTooltip => Get("Update_DismissTooltip");
    public string UpdateManualNote => Get("Update_ManualNote");
    public string UpdateInstalling => Get("Update_Installing");
    public string UpdateUpToDate => Get("Update_UpToDate");
    public string UpdateChecking => Get("Update_Checking");
    public string UpdateCheckButton => Get("Update_CheckButton");
    public string UpdateAutoCheck => Get("Update_AutoCheck");

    public string PhotosCount(int count) => string.Format(Get("Content_PhotosCountFormat"), count);
    public string PhotosFound(int count) => string.Format(Get("Status_PhotosFoundFormat"), count);
    public string Importing(string fileName, int completed, int total) =>
        string.Format(Get("Status_ImportingFormat"), fileName, completed, total);
    public string ImportError(string message) => string.Format(Get("Import_ErrorFormat"), message);
    public string DeleteFailedNote(string message) => string.Format(Get("Import_DeleteFailedFormat"), message);
    public string UpdateAvailable(string newVersion, string currentVersion) =>
        string.Format(Get("Update_AvailableFormat"), newVersion, currentVersion);
    public string UpdateDownloading(double fraction) => string.Format(Get("Update_DownloadingFormat"), fraction);
    public string UpdateError(string message) => string.Format(Get("Update_ErrorFormat"), message);
    public string UpdateCheckFailed(string message) => string.Format(Get("Update_CheckFailedFormat"), message);
    public string ImportButton(int count) => count switch
    {
        0 => ImportButtonDefault,
        1 => Get("Import_ButtonOne"),
        _ => string.Format(Get("Import_ButtonFormat"), count),
    };
    public string VersionLabel(string version) => string.Format(Get("Update_VersionFormat"), version);

    public string OrganizationDisplayName(FolderOrganizationScheme scheme) => scheme switch
    {
        FolderOrganizationScheme.YearMonth => OrgYearMonth,
        FolderOrganizationScheme.YearMonthDay => OrgYearMonthDay,
        FolderOrganizationScheme.ByCameraModel => OrgByCameraModel,
        _ => OrgFlat,
    };

    public string GetStatusLabel(ImportStatus status) => status switch
    {
        ImportStatus.Imported => Get("StatusLabel_Imported"),
        ImportStatus.Duplicate => Get("StatusLabel_Duplicate"),
        ImportStatus.Error => Get("StatusLabel_Error"),
        _ => Get("StatusLabel_Pending"),
    };
}

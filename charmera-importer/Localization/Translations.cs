using System.Collections.Generic;

namespace charmera_importer.Localization;

// Single source of truth for every user-facing string. Keep Portuguese and English
// in sync — LocalizedStrings falls back to the raw key if a lookup misses, which is
// visible enough during development to catch a missed translation.
internal static class Translations
{
    private static readonly Dictionary<string, string> Portuguese = new()
    {
        ["Header_DevicePlaceholder"] = "Selecione a câmera",
        ["Header_RefreshTooltip"] = "Atualizar lista de dispositivos",

        ["Sidebar_NoDestinationSelected"] = "Nenhuma pasta escolhida",
        ["Sidebar_BrowseButton"] = "Escolher pasta...",
        ["Sidebar_OrganizeLabel"] = "Organizar em pastas",
        ["Sidebar_NamingLabel"] = "Nome do arquivo",
        ["Sidebar_KeepOriginalName"] = "Manter nome original como sufixo",
        ["Sidebar_DeleteAfterImport"] = "Apagar da câmera após importar",
        ["Sidebar_DeleteAfterImportWarning"] = "Remove permanentemente as fotos da câmera depois de confirmar a importação. Não pode ser desfeito.",
        ["Sidebar_ImportButton"] = "Importar fotos",

        ["Content_PhotosTitle"] = "Fotos",
        ["Content_PhotosCountFormat"] = "{0} foto(s)",

        ["Detail_Title"] = "Detalhes EXIF",
        ["Detail_Make"] = "Marca",
        ["Detail_Model"] = "Modelo",
        ["Detail_Date"] = "Data",
        ["Detail_Dimensions"] = "Dimensões",
        ["Detail_NoExifNote"] = "Esta câmera não grava marca/modelo/data no EXIF.",
        ["Detail_AllTags"] = "Todas as tags",

        ["Org_YearMonth"] = "Ano/Mês (2026/03)",
        ["Org_YearMonthDay"] = "Ano/Mês/Dia (2026/03/15)",
        ["Org_ByCameraModel"] = "Por modelo de câmera",
        ["Org_Flat"] = "Pasta única (sem subpastas)",

        ["Status_Scanning"] = "Procurando fotos...",
        ["Status_PhotosFoundFormat"] = "{0} foto(s) encontrada(s)",
        ["Status_ImportingFormat"] = "Importando {0} ({1}/{2})",
        ["Status_ImportComplete"] = "Importação concluída",

        ["Import_AlreadyImported"] = "Já importada anteriormente",
        ["Import_AlreadyAtDestination"] = "Já existe no destino",
        ["Import_Success"] = "Importada",
        ["Import_ErrorFormat"] = "Erro: {0}",
        ["Import_DeleteFailedFormat"] = "falha ao apagar da câmera: {0}",

        ["StatusLabel_Imported"] = "Importada",
        ["StatusLabel_Duplicate"] = "Duplicada",
        ["StatusLabel_Error"] = "Erro",
        ["StatusLabel_Pending"] = "Pendente",

        ["FolderPicker_Title"] = "Escolher pasta de destino",

        ["Update_AvailableFormat"] = "Nova versão disponível: {0} (você está na {1})",
        ["Update_InstallButton"] = "Atualizar agora",
        ["Update_DownloadButton"] = "Baixar",
        ["Update_ReleaseNotesButton"] = "Novidades",
        ["Update_DismissTooltip"] = "Dispensar",
        ["Update_ManualNote"] = "Esta instalação não pode se atualizar sozinha (ex.: pacote .deb/.rpm) — baixe a nova versão na página da release.",
        ["Update_DownloadingFormat"] = "Baixando atualização... {0:P0}",
        ["Update_Installing"] = "Instalando atualização, o app vai reiniciar...",
        ["Update_ErrorFormat"] = "Falha ao atualizar: {0}",
        ["Update_UpToDate"] = "Você já está na versão mais recente.",
        ["Update_CheckFailedFormat"] = "Não foi possível verificar atualizações: {0}",
        ["Update_Checking"] = "Verificando atualizações...",
        ["Update_CheckButton"] = "Verificar agora",
        ["Update_AutoCheck"] = "Verificar atualizações ao abrir",
        ["Update_VersionFormat"] = "Charmera Importer {0}",

        ["Step_Source"] = "Câmera",
        ["Step_Destination"] = "Destino",
        ["Step_AfterImport"] = "Depois de importar",
        ["Source_NoDevice"] = "Nenhuma câmera selecionada",
        ["Dest_ChangeButton"] = "Alterar",
        ["Dest_PreviewLabel"] = "Exemplo de caminho",
        ["Import_ButtonOne"] = "Importar 1 foto",
        ["Import_ButtonFormat"] = "Importar {0} fotos",
        ["Hint_SelectCamera"] = "Selecione a câmera para começar.",
        ["Hint_NoPhotos"] = "Não há fotos nesta câmera para importar.",
        ["Hint_ChooseDestination"] = "Escolha a pasta de destino para continuar.",
        ["Empty_NoDeviceTitle"] = "Conecte sua câmera",
        ["Empty_NoDeviceSubtitle"] = "Ligue a câmera via USB e selecione-a no painel ao lado.",
        ["Empty_NoPhotosTitle"] = "Nenhuma foto encontrada",
        ["Empty_NoPhotosSubtitle"] = "As fotos são procuradas na pasta DCIM do dispositivo.",
        ["Settings_Title"] = "Configurações",
        ["Settings_Language"] = "Idioma",
        ["Settings_Updates"] = "Atualizações",
        ["Detail_CloseTooltip"] = "Fechar",
    };

    private static readonly Dictionary<string, string> English = new()
    {
        ["Header_DevicePlaceholder"] = "Select your camera",
        ["Header_RefreshTooltip"] = "Refresh device list",

        ["Sidebar_NoDestinationSelected"] = "No folder chosen",
        ["Sidebar_BrowseButton"] = "Choose folder...",
        ["Sidebar_OrganizeLabel"] = "Organize into folders",
        ["Sidebar_NamingLabel"] = "File name",
        ["Sidebar_KeepOriginalName"] = "Keep original filename as suffix",
        ["Sidebar_DeleteAfterImport"] = "Delete from camera after importing",
        ["Sidebar_DeleteAfterImportWarning"] = "Permanently removes photos from the camera once the import is confirmed. This cannot be undone.",
        ["Sidebar_ImportButton"] = "Import photos",

        ["Content_PhotosTitle"] = "Photos",
        ["Content_PhotosCountFormat"] = "{0} photo(s)",

        ["Detail_Title"] = "EXIF details",
        ["Detail_Make"] = "Make",
        ["Detail_Model"] = "Model",
        ["Detail_Date"] = "Date",
        ["Detail_Dimensions"] = "Dimensions",
        ["Detail_NoExifNote"] = "This camera doesn't write make/model/date to EXIF.",
        ["Detail_AllTags"] = "All tags",

        ["Org_YearMonth"] = "Year/Month (2026/03)",
        ["Org_YearMonthDay"] = "Year/Month/Day (2026/03/15)",
        ["Org_ByCameraModel"] = "By camera model",
        ["Org_Flat"] = "Single folder (no subfolders)",

        ["Status_Scanning"] = "Looking for photos...",
        ["Status_PhotosFoundFormat"] = "{0} photo(s) found",
        ["Status_ImportingFormat"] = "Importing {0} ({1}/{2})",
        ["Status_ImportComplete"] = "Import complete",

        ["Import_AlreadyImported"] = "Already imported before",
        ["Import_AlreadyAtDestination"] = "Already exists at destination",
        ["Import_Success"] = "Imported",
        ["Import_ErrorFormat"] = "Error: {0}",
        ["Import_DeleteFailedFormat"] = "failed to delete from camera: {0}",

        ["StatusLabel_Imported"] = "Imported",
        ["StatusLabel_Duplicate"] = "Duplicate",
        ["StatusLabel_Error"] = "Error",
        ["StatusLabel_Pending"] = "Pending",

        ["FolderPicker_Title"] = "Choose destination folder",

        ["Update_AvailableFormat"] = "New version available: {0} (you have {1})",
        ["Update_InstallButton"] = "Update now",
        ["Update_DownloadButton"] = "Download",
        ["Update_ReleaseNotesButton"] = "What's new",
        ["Update_DismissTooltip"] = "Dismiss",
        ["Update_ManualNote"] = "This installation can't update itself (e.g. a .deb/.rpm package) — download the new version from the release page.",
        ["Update_DownloadingFormat"] = "Downloading update... {0:P0}",
        ["Update_Installing"] = "Installing update, the app will restart...",
        ["Update_ErrorFormat"] = "Update failed: {0}",
        ["Update_UpToDate"] = "You're on the latest version.",
        ["Update_CheckFailedFormat"] = "Couldn't check for updates: {0}",
        ["Update_Checking"] = "Checking for updates...",
        ["Update_CheckButton"] = "Check now",
        ["Update_AutoCheck"] = "Check for updates on startup",
        ["Update_VersionFormat"] = "Charmera Importer {0}",

        ["Step_Source"] = "Camera",
        ["Step_Destination"] = "Destination",
        ["Step_AfterImport"] = "After import",
        ["Source_NoDevice"] = "No camera selected",
        ["Dest_ChangeButton"] = "Change",
        ["Dest_PreviewLabel"] = "Example path",
        ["Import_ButtonOne"] = "Import 1 photo",
        ["Import_ButtonFormat"] = "Import {0} photos",
        ["Hint_SelectCamera"] = "Select your camera to get started.",
        ["Hint_NoPhotos"] = "There are no photos on this camera to import.",
        ["Hint_ChooseDestination"] = "Choose a destination folder to continue.",
        ["Empty_NoDeviceTitle"] = "Connect your camera",
        ["Empty_NoDeviceSubtitle"] = "Plug it in via USB and select it in the panel on the left.",
        ["Empty_NoPhotosTitle"] = "No photos found",
        ["Empty_NoPhotosSubtitle"] = "Photos are read from the device's DCIM folder.",
        ["Settings_Title"] = "Settings",
        ["Settings_Language"] = "Language",
        ["Settings_Updates"] = "Updates",
        ["Detail_CloseTooltip"] = "Close",
    };

    public static IReadOnlyDictionary<string, string> Get(string languageCode) => languageCode switch
    {
        "pt" => Portuguese,
        _ => English,
    };
}

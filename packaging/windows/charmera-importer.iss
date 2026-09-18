; Inno Setup script for the Windows installer. Built by .github/workflows/release.yml on a
; Windows runner; to build by hand (Inno Setup 6, from the repo root):
;
;   iscc /DAppVersion=0.1.0 /DSourceExe=<published charmera-importer.exe> /DOutputDir=dist ^
;        packaging\windows\charmera-importer.iss
;
; Per-user install (no admin prompt) into %LocalAppData%\Programs\Charmera Importer. The AppId
; must never change: it's how a newer Setup.exe recognises and upgrades an existing install,
; which the in-app self-updater relies on (it runs this installer with /SILENT).

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef SourceExe
  #error Pass /DSourceExe=<path to the published charmera-importer.exe>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppName "Charmera Importer"
#define AppExeName "charmera-importer.exe"
#define RepoUrl "https://github.com/RickCaleg/charmera-importer"

[Setup]
AppId={{D0A8E479-F257-4D77-A088-C0E9A77BADDA}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Richardson Calegari Saconi
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\charmera-importer\Assets\charmera-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=charmera-importer-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExeName}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Interactive install: the usual "Launch Charmera Importer" checkbox on the last page.
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Silent install (the in-app self-updater): relaunch the app automatically once upgraded.
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: WizardSilent

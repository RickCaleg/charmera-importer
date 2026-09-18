using System;

namespace charmera_importer.Models;

public enum UpdateInstallMode
{
    // The app can download, verify, and install the new version itself, then restart.
    Automatic,

    // The app can't replace itself (installed via .deb/.rpm, running from a dev build, or no
    // matching release asset for this OS/architecture) — the user is sent to the release page.
    Manual,
}

public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string ReleasePageUrl,
    string? AssetName,
    string? AssetUrl,
    string? ChecksumsUrl,
    UpdateInstallMode InstallMode);

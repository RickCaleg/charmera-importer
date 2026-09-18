using System;
using System.Threading;
using System.Threading.Tasks;
using charmera_importer.Models;

namespace charmera_importer.Services;

public interface IUpdateService
{
    Version CurrentVersion { get; }

    // Returns null when already on the latest release. Throws on network/API failures.
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);

    // Downloads the release asset, verifies it against the release's SHA256SUMS, installs it,
    // and launches the new version. The caller must shut the app down right after this
    // returns. Only valid for UpdateInstallMode.Automatic.
    Task InstallUpdateAsync(UpdateInfo update, IProgress<double> progress, CancellationToken ct = default);

    void OpenReleasePage(UpdateInfo update);
}

using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using charmera_importer.Models;

namespace charmera_importer.Services;

// Self-update against this repo's GitHub Releases. Every downloaded asset is checked against
// the release's SHA256SUMS before anything is installed — a missing or mismatched checksum
// aborts the update rather than running an unverified binary.
//
// How the new version gets installed depends on how this copy was installed:
//   - Windows (Inno Setup install): run the new Setup.exe silently; it closes this app,
//     upgrades in place (same AppId), and relaunches it.
//   - Linux portable tarball in a user-writable folder: stage the new binary next to the
//     running one; once the app exits, a small shell helper renames it into place and
//     relaunches it (see ReplaceLinuxExecutableAsync for why it can't happen in-process).
//   - Linux .deb/.rpm (/opt, /usr), dev builds, other architectures: Manual — the package
//     manager owns those files, so the user is sent to the release page instead.
public sealed class GitHubUpdateService : IUpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/RickCaleg/charmera-importer/releases/latest";
    private const string LinuxExecutableName = "charmera-importer";

    private static readonly HttpClient Http = CreateHttpClient();

    public Version CurrentVersion { get; } =
        ReleaseMetadata.Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        var json = await Http.GetStringAsync(LatestReleaseApiUrl, timeout.Token);
        var release = ReleaseMetadata.ParseRelease(json)
            ?? throw new InvalidOperationException("Unexpected response from the GitHub releases API.");

        var latestVersion = ReleaseMetadata.ParseTagVersion(release.TagName);
        if (latestVersion is null || !ReleaseMetadata.IsNewer(latestVersion, CurrentVersion))
        {
            return null;
        }

        var assetSuffix = OperatingSystem.IsWindows() ? ReleaseMetadata.WindowsAssetSuffix
            : OperatingSystem.IsLinux() ? ReleaseMetadata.LinuxAssetSuffix
            : null;
        var asset = assetSuffix is null ? null : ReleaseMetadata.FindAsset(release, assetSuffix);
        var checksums = ReleaseMetadata.FindAsset(release, ReleaseMetadata.ChecksumsAssetName);

        var installMode = asset is not null && checksums is not null && CanSelfInstall()
            ? UpdateInstallMode.Automatic
            : UpdateInstallMode.Manual;

        return new UpdateInfo(
            latestVersion,
            release.TagName,
            release.HtmlUrl,
            asset?.Name,
            asset?.DownloadUrl,
            checksums?.DownloadUrl,
            installMode);
    }

    public async Task InstallUpdateAsync(UpdateInfo update, IProgress<double> progress, CancellationToken ct = default)
    {
        if (update.InstallMode != UpdateInstallMode.Automatic
            || update.AssetName is null || update.AssetUrl is null || update.ChecksumsUrl is null)
        {
            throw new InvalidOperationException("This update can't be installed automatically.");
        }

        var checksumsText = await Http.GetStringAsync(update.ChecksumsUrl, ct);
        var expectedHash = ReleaseMetadata.FindExpectedHash(checksumsText, update.AssetName)
            ?? throw new InvalidOperationException($"{update.AssetName} is not listed in the release's SHA256SUMS.");

        var downloadPath = Path.Combine(Path.GetTempPath(), $"charmera-importer-update-{update.Version}-{update.AssetName}");
        try
        {
            var actualHash = await DownloadAsync(update.AssetUrl, downloadPath, progress, ct);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Downloaded update failed checksum verification.");
            }

            if (OperatingSystem.IsWindows())
            {
                // The installer outlives this process, so it must not be deleted below.
                LaunchWindowsInstaller(downloadPath);
                downloadPath = null;
            }
            else
            {
                await ReplaceLinuxExecutableAsync(downloadPath, ct);
            }
        }
        finally
        {
            if (downloadPath is not null && File.Exists(downloadPath))
            {
                File.Delete(downloadPath);
            }
        }
    }

    public void OpenReleasePage(UpdateInfo update)
    {
        Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"charmera-importer/{version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    // Returns the lowercase hex SHA-256 of the downloaded file, computed while streaming.
    private static async Task<string> DownloadAsync(string url, string destinationPath, IProgress<double> progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destinationPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        long receivedBytes = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            hash.AppendData(buffer, 0, read);
            receivedBytes += read;
            if (totalBytes is > 0)
            {
                progress.Report((double)receivedBytes / totalBytes.Value);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void LaunchWindowsInstaller(string installerPath)
    {
        // Inno Setup flags: no "This will install..." prompt, progress window only, close the
        // running app if it's still up. The .iss relaunches the app after a silent install.
        Process.Start(new ProcessStartInfo(installerPath, "/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
        {
            UseShellExecute = true,
        });
    }

    // The swap itself must happen after this process exits, not here: a single-file .NET app
    // lazily loads framework assemblies out of its own bundle *by path*, so once a new binary
    // sits at that path any later assembly load (even Process.Start's) reads the wrong file and
    // crashes. So this only stages the new binary; a detached shell helper waits for this PID
    // to exit, renames it into place, and relaunches it.
    [UnsupportedOSPlatform("windows")]
    private static async Task ReplaceLinuxExecutableAsync(string tarballPath, CancellationToken ct)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable's path.");
        var installDirectory = Path.GetDirectoryName(executablePath)!;

        // Staged inside the install directory so the final rename is same-filesystem (atomic),
        // never a cross-device copy that could leave a half-written binary.
        var stagingDirectory = Path.Combine(installDirectory, $".charmera-importer-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            await using (var file = File.OpenRead(tarballPath))
            await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                await TarFile.ExtractToDirectoryAsync(gzip, stagingDirectory, overwriteFiles: true, ct);
            }

            var newExecutable = Path.Combine(stagingDirectory, LinuxExecutableName);
            if (!File.Exists(newExecutable))
            {
                throw new InvalidOperationException("The update package doesn't contain the application binary.");
            }

            File.SetUnixFileMode(newExecutable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            // $1 = our PID, $2 = staged binary, $3 = installed binary, $4 = staging dir. If the
            // rename fails the old binary is still relaunched, so the user never ends up with
            // no app at all.
            var helper = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            helper.ArgumentList.Add("-c");
            helper.ArgumentList.Add(
                "while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; " +
                "mv -f \"$2\" \"$3\"; rm -rf \"$4\"; exec \"$3\"");
            helper.ArgumentList.Add("charmera-importer-updater");
            helper.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            helper.ArgumentList.Add(newExecutable);
            helper.ArgumentList.Add(executablePath);
            helper.ArgumentList.Add(stagingDirectory);
            Process.Start(helper);
        }
        catch
        {
            Directory.Delete(stagingDirectory, recursive: true);
            throw;
        }
    }

    private static bool CanSelfInstall()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || Environment.ProcessPath is not { } executablePath)
        {
            return false;
        }

        // A single-file release build has no charmera-importer.dll beside the executable; a
        // `dotnet run`/`dotnet build` output does, and must never overwrite itself.
        var directory = Path.GetDirectoryName(executablePath)!;
        if (File.Exists(Path.Combine(directory, "charmera-importer.dll")))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        if (!OperatingSystem.IsLinux()
            || executablePath.StartsWith("/opt/", StringComparison.Ordinal)
            || executablePath.StartsWith("/usr/", StringComparison.Ordinal))
        {
            return false;
        }

        return IsDirectoryWritable(directory);
    }

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            var probePath = Path.Combine(directory, $".charmera-importer-write-test-{Guid.NewGuid():N}");
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

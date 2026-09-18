using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace charmera_importer.Services;

public sealed record GitHubReleaseAsset(string Name, string DownloadUrl);

public sealed record GitHubRelease(string TagName, string HtmlUrl, IReadOnlyList<GitHubReleaseAsset> Assets);

// Pure parsing/comparison helpers for the self-update flow, kept free of I/O so the rules
// (version comparison, asset naming, checksum lookup) can be unit-tested directly.
public static class ReleaseMetadata
{
    // Release asset names produced by .github/workflows/release.yml — keep the two in sync.
    public const string LinuxAssetSuffix = "-linux-x64.tar.gz";
    public const string WindowsAssetSuffix = "-win-x64-setup.exe";
    public const string ChecksumsAssetName = "SHA256SUMS";

    public static GitHubRelease? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.GetString() is not { } tagName
            || !root.TryGetProperty("html_url", out var urlElement) || urlElement.GetString() is not { } htmlUrl)
        {
            return null;
        }

        var assets = new List<GitHubReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var nameElement) && nameElement.GetString() is { } name
                    && asset.TryGetProperty("browser_download_url", out var downloadElement) && downloadElement.GetString() is { } downloadUrl)
                {
                    assets.Add(new GitHubReleaseAsset(name, downloadUrl));
                }
            }
        }

        return new GitHubRelease(tagName, htmlUrl, assets);
    }

    // Accepts "v1.2.3" / "1.2.3" / "1.2"; anything after a '-' or '+' (pre-release/build
    // metadata) is ignored. Returns null for tags that aren't a version at all.
    public static Version? ParseTagVersion(string tag)
    {
        var trimmed = tag.Trim().TrimStart('v', 'V');
        var cut = trimmed.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        return Version.TryParse(trimmed, out var version) ? Normalize(version) : null;
    }

    // Collapses to major.minor.patch so "0.1.0" (from a tag) and "0.1.0.0" (from the assembly)
    // compare as equal — System.Version otherwise treats a missing component as smaller.
    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    public static bool IsNewer(Version candidate, Version current) =>
        Normalize(candidate) > Normalize(current);

    public static GitHubReleaseAsset? FindAsset(GitHubRelease release, string suffix)
    {
        foreach (var asset in release.Assets)
        {
            if (asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        return null;
    }

    // Parses `sha256sum` output ("<hex>  <name>", or "<hex> *<name>" in binary mode).
    public static string? FindExpectedHash(string checksumsText, string assetName)
    {
        using var reader = new StringReader(checksumsText);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == assetName && parts[0].Length == 64)
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }
}

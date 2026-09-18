using charmera_importer.Services;

namespace charmera_importer.Tests;

public class ReleaseMetadataTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V0.2", "0.2.0")]
    [InlineData("v1.0.0-beta.1", "1.0.0")]
    [InlineData("v1.0.0+build.5", "1.0.0")]
    public void ParseTagVersion_AcceptsCommonTagShapes(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), ReleaseMetadata.ParseTagVersion(tag));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    public void ParseTagVersion_ReturnsNullForNonVersions(string tag)
    {
        Assert.Null(ReleaseMetadata.ParseTagVersion(tag));
    }

    [Fact]
    public void IsNewer_TreatsThreeAndFourComponentVersionsAsEqual()
    {
        // Assembly versions carry a 4th component; release tags usually don't.
        Assert.False(ReleaseMetadata.IsNewer(new Version(0, 1, 0), new Version(0, 1, 0, 0)));
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("0.1.0", "0.2.0", false)]
    public void IsNewer_ComparesNumerically(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, ReleaseMetadata.IsNewer(Version.Parse(candidate), Version.Parse(current)));
    }

    [Fact]
    public void ParseRelease_ReadsTagUrlAndAssets()
    {
        const string json = """
            {
              "tag_name": "v0.2.0",
              "html_url": "https://github.com/RickCaleg/charmera-importer/releases/tag/v0.2.0",
              "assets": [
                { "name": "charmera-importer-0.2.0-linux-x64.tar.gz", "browser_download_url": "https://example.test/linux" },
                { "name": "charmera-importer-0.2.0-win-x64-setup.exe", "browser_download_url": "https://example.test/win" },
                { "name": "SHA256SUMS", "browser_download_url": "https://example.test/sums" }
              ]
            }
            """;

        var release = ReleaseMetadata.ParseRelease(json);

        Assert.NotNull(release);
        Assert.Equal("v0.2.0", release.TagName);
        Assert.Equal(3, release.Assets.Count);
        Assert.Equal("https://example.test/linux", ReleaseMetadata.FindAsset(release, ReleaseMetadata.LinuxAssetSuffix)?.DownloadUrl);
        Assert.Equal("https://example.test/win", ReleaseMetadata.FindAsset(release, ReleaseMetadata.WindowsAssetSuffix)?.DownloadUrl);
        Assert.Equal("https://example.test/sums", ReleaseMetadata.FindAsset(release, ReleaseMetadata.ChecksumsAssetName)?.DownloadUrl);
    }

    [Fact]
    public void ParseRelease_ReturnsNullWithoutTag()
    {
        Assert.Null(ReleaseMetadata.ParseRelease("""{ "message": "Not Found" }"""));
    }

    [Fact]
    public void FindExpectedHash_ReadsSha256sumOutput()
    {
        var hashA = new string('a', 64);
        var hashB = new string('B', 64);
        var text = $"{hashA}  charmera-importer-0.2.0-linux-x64.tar.gz\n{hashB} *charmera-importer-0.2.0-win-x64-setup.exe\n";

        Assert.Equal(hashA, ReleaseMetadata.FindExpectedHash(text, "charmera-importer-0.2.0-linux-x64.tar.gz"));
        Assert.Equal(hashB.ToLowerInvariant(), ReleaseMetadata.FindExpectedHash(text, "charmera-importer-0.2.0-win-x64-setup.exe"));
    }

    [Fact]
    public void FindExpectedHash_DoesNotMatchPartialNames()
    {
        var text = $"{new string('a', 64)}  evil-charmera-importer-0.2.0-linux-x64.tar.gz\n";

        Assert.Null(ReleaseMetadata.FindExpectedHash(text, "charmera-importer-0.2.0-linux-x64.tar.gz"));
    }
}

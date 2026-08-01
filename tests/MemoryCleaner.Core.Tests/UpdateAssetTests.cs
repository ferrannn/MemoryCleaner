using System.Text.Json;
using MemoryCleaner.Core;
using Xunit;

namespace MemoryCleaner.Core.Tests;

/// <summary>
/// UpdateChecker 资产配对与 digest 解析逻辑测试：
/// exe 资产、其 .sha256 校验资产、GitHub API digest 主校验路径。
/// </summary>
public class UpdateAssetTests
{
    private const string HashHex = "2554d95faaa573d2b1fe7d6f462bdb30564fa6d2e1c7d2e2c706ce31ab8e91ac";

    private static UpdateChecker.ReleaseAsset Asset(string name, string? digest = null) =>
        new(name, $"https://github.com/ferrannn/MemoryCleaner/releases/download/v1.3.7/{name}", 100, digest);

    private static UpdateChecker.ReleaseInfo Release(params UpdateChecker.ReleaseAsset[] assets) =>
        new("v1.3.7", "v1.3.7", "", "https://github.com/ferrannn/MemoryCleaner/releases/v1.3.7", assets);

    // ---------- PickChecksumAsset（侧车资产解析，兜底路径） ----------

    [Fact]
    public void PickChecksumAsset_ExactName_ReturnsAsset()
    {
        var exe = Asset("MemoryCleaner.exe");
        var sha = Asset("MemoryCleaner.exe.sha256");
        var rel = Release(exe, sha);

        Assert.Same(sha, UpdateChecker.PickChecksumAsset(rel, exe));
    }

    [Fact]
    public void PickChecksumAsset_SoleSha256Fallback()
    {
        var exe = Asset("MemoryCleaner.exe");
        var sha = Asset("checksums.sha256");
        var rel = Release(exe, sha);

        Assert.Same(sha, UpdateChecker.PickChecksumAsset(rel, exe));
    }

    [Fact]
    public void PickChecksumAsset_NoSha256_ReturnsNull()
    {
        var exe = Asset("MemoryCleaner.exe");
        var rel = Release(exe);

        Assert.Null(UpdateChecker.PickChecksumAsset(rel, exe));
    }

    [Fact]
    public void PickChecksumAsset_NoAssets_ReturnsNull()
    {
        var exe = Asset("MemoryCleaner.exe");
        var rel = Release();

        Assert.Null(UpdateChecker.PickChecksumAsset(rel, exe));
    }

    [Fact]
    public void PickChecksumAsset_MultipleSha256_ReturnsNull()
    {
        var exe = Asset("MemoryCleaner.exe");
        var rel = Release(exe, Asset("a.sha256"), Asset("b.sha256"));

        Assert.Null(UpdateChecker.PickChecksumAsset(rel, exe));
    }

    // ---------- ParseDigest（GitHub API digest 主校验路径） ----------

    [Fact]
    public void ParseDigest_ValidSha256_ReturnsHex()
    {
        Assert.Equal(HashHex, UpdateChecker.ParseDigest($"sha256:{HashHex}"));
    }

    [Fact]
    public void ParseDigest_UppercaseSha256Prefix_ReturnsHex()
    {
        Assert.Equal(HashHex, UpdateChecker.ParseDigest($"SHA256:{HashHex}"));
    }

    [Fact]
    public void ParseDigest_NonSha256Algorithm_ReturnsNull()
    {
        Assert.Null(UpdateChecker.ParseDigest($"sha1:{new string('a', 40)}"));
        Assert.Null(UpdateChecker.ParseDigest($"md5:{new string('a', 32)}"));
    }

    [Fact]
    public void ParseDigest_InvalidHex_ReturnsNull()
    {
        Assert.Null(UpdateChecker.ParseDigest($"sha256:{new string('g', 64)}"));
        Assert.Null(UpdateChecker.ParseDigest($"sha256:abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseDigest_NullOrEmpty_ReturnsNull(string? digest)
        => Assert.Null(UpdateChecker.ParseDigest(digest));

    // ---------- PickUpdateAssetsAsync（主路径 digest / 兜底侧车） ----------

    [Fact]
    public async Task PickUpdateAssets_ExeWithDigest_ReturnsDigest()
    {
        var exe = Asset("MemoryCleaner.exe", $"sha256:{HashHex}");
        var rel = Release(exe);

        var (exeOut, expected) = await UpdateChecker.PickUpdateAssetsAsync(rel);
        Assert.Same(exe, exeOut);
        Assert.Equal(HashHex, expected);
    }

    [Fact]
    public async Task PickUpdateAssets_ExeWithInvalidDigest_NoSidecar_ReturnsNull()
    {
        // digest 非法 + 无侧车 → 校验和不可用（fail-closed）
        var exe = Asset("MemoryCleaner.exe", "sha256:not-hex");
        var rel = Release(exe);

        var (exeOut, expected) = await UpdateChecker.PickUpdateAssetsAsync(rel);
        Assert.Same(exe, exeOut);
        Assert.Null(expected);
    }

    [Fact]
    public async Task PickUpdateAssets_NoExe_ReturnsNullTuple()
    {
        var rel = Release(Asset("readme.txt"));

        var (exeOut, expected) = await UpdateChecker.PickUpdateAssetsAsync(rel);
        Assert.Null(exeOut);
        Assert.Null(expected);
    }

    [Fact]
    public async Task PickUpdateAssets_DigestPrecedence_OverSidecar()
    {
        // digest 存在时优先使用，不走侧车（且不触发网络）
        var exe = Asset("MemoryCleaner.exe", $"sha256:{HashHex}");
        var sha = Asset("MemoryCleaner.exe.sha256"); // 侧车内容会是另一个哈希
        var rel = Release(exe, sha);

        var (exeOut, expected) = await UpdateChecker.PickUpdateAssetsAsync(rel);
        Assert.Same(exe, exeOut);
        Assert.Equal(HashHex, expected); // digest 优先
    }

    // ---------- JSON 反序列化（digest 从 GitHub API 响应的映射） ----------

    [Fact]
    public void DeserializeAsset_DigestField_MapsCorrectly()
    {
        // 与真实 GitHub API 响应一致的结构
        string json = $$"""
            {
              "name": "MemoryCleaner.exe",
              "browser_download_url": "https://github.com/ferrannn/MemoryCleaner/releases/download/v1.3.7/MemoryCleaner.exe",
              "size": 71827474,
              "digest": "sha256:{{HashHex}}"
            }
            """;

        var asset = JsonSerializer.Deserialize<UpdateChecker.ReleaseAsset>(json);
        Assert.NotNull(asset);
        Assert.Equal("MemoryCleaner.exe", asset.Name);
        Assert.Equal(71827474, asset.Size);
        Assert.Equal($"sha256:{HashHex}", asset.Digest);
        Assert.Equal(HashHex, UpdateChecker.ParseDigest(asset.Digest));
    }

    [Fact]
    public void DeserializeAsset_DigestMissing_IsNull()
    {
        // 老版 API / 镜像不返回 digest 字段 → null（触发兜底路径）
        string json = """
            { "name": "MemoryCleaner.exe",
              "browser_download_url": "https://example.com/MemoryCleaner.exe",
              "size": 100 }
            """;

        var asset = JsonSerializer.Deserialize<UpdateChecker.ReleaseAsset>(json);
        Assert.NotNull(asset);
        Assert.Null(asset.Digest);
    }
}

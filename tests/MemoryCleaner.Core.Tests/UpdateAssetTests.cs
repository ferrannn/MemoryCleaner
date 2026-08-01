using MemoryCleaner.Core;
using Xunit;

namespace MemoryCleaner.Core.Tests;

/// <summary>
/// UpdateChecker 中资产配对逻辑的测试：exe 资产与其 .sha256 校验资产的解析。
/// </summary>
public class UpdateAssetTests
{
    private static UpdateChecker.ReleaseAsset Asset(string name) =>
        new(name, $"https://github.com/ferrannn/MemoryCleaner/releases/download/v1.3.7/{name}", 100);

    private static UpdateChecker.ReleaseInfo Release(params UpdateChecker.ReleaseAsset[] assets) =>
        new("v1.3.7", "v1.3.7", "", "https://github.com/ferrannn/MemoryCleaner/releases/v1.3.7", assets);

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

    [Fact]
    public void PickUpdateAssets_ExePlusChecksum()
    {
        var exe = Asset("MemoryCleaner.exe");
        var sha = Asset("MemoryCleaner.exe.sha256");
        var rel = Release(exe, sha);

        var (exeOut, checksumOut) = UpdateChecker.PickUpdateAssets(rel);
        Assert.Same(exe, exeOut);
        Assert.Same(sha, checksumOut);
    }

    [Fact]
    public void PickUpdateAssets_NoExe_ReturnsNullTuple()
    {
        var rel = Release(Asset("readme.txt"));

        var (exeOut, checksumOut) = UpdateChecker.PickUpdateAssets(rel);
        Assert.Null(exeOut);
        Assert.Null(checksumOut);
    }

    [Fact]
    public void PickUpdateAssets_ExeWithoutChecksum()
    {
        var exe = Asset("MemoryCleaner.exe");
        var rel = Release(exe);

        var (exeOut, checksumOut) = UpdateChecker.PickUpdateAssets(rel);
        Assert.Same(exe, exeOut);
        Assert.Null(checksumOut);
    }
}

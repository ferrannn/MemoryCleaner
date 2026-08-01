using MemoryCleaner.Core;
using Xunit;

namespace MemoryCleaner.Core.Tests;

/// <summary>
/// ChecksumVerifier 纯逻辑测试。
/// 重点是 fail-closed 行为：任何异常输入都必须返回 null/false，
/// 绝不能让调用方带着不可信哈希往下走。
/// </summary>
public class ChecksumVerifierTests
{
    private const string Exe = "MemoryCleaner.exe";
    // SHA256("abc")
    private const string HashAbc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    // ---------- IsValidSha256Hex ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a")] // 63 hex
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad0")] // 65 hex
    [InlineData("ga7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")] // 非 hex
    public void IsValidSha256Hex_Invalid_ReturnsFalse(string? s)
        => Assert.False(ChecksumVerifier.IsValidSha256Hex(s));

    [Theory]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")] // 小写
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")] // 大写
    public void IsValidSha256Hex_Valid_ReturnsTrue(string s)
        => Assert.True(ChecksumVerifier.IsValidSha256Hex(s));

    // ---------- TryParse ----------

    [Fact]
    public void TryParse_ValidShasumLine_ReturnsHash()
    {
        string content = $"{HashAbc}  {Exe}";
        Assert.Equal(HashAbc, ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_BinaryStarFormat_ReturnsHash()
    {
        string content = $"{HashAbc} *{Exe}";
        Assert.Equal(HashAbc, ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_BareHash_ReturnsHash()
    {
        Assert.Equal(HashAbc, ChecksumVerifier.TryParse(HashAbc, Exe));
    }

    [Fact]
    public void TryParse_UppercaseHash_ReturnsLowercase()
    {
        string content = $"{HashAbc.ToUpperInvariant()}  {Exe}";
        Assert.Equal(HashAbc, ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_WrongFileName_ReturnsNull()
    {
        // 只点名了别的文件 → 不能用，必须拒绝
        string content = $"{HashAbc}  Other.exe";
        Assert.Null(ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_MultiLine_MatchingLineWins()
    {
        string other = new('1', 64);
        string content = $"{other}  Other.exe\n{HashAbc}  {Exe}";
        Assert.Equal(HashAbc, ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_ShortHash_ReturnsNull()
    {
        string content = $"{new string('a', 63)}  {Exe}";
        Assert.Null(ChecksumVerifier.TryParse(content, Exe));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    [InlineData("﻿")] // 仅 BOM
    public void TryParse_EmptyOrNull_ReturnsNull(string? content)
        => Assert.Null(ChecksumVerifier.TryParse(content, Exe));

    [Fact]
    public void TryParse_BomPrefix_ReturnsHash()
    {
        string content = $"﻿{HashAbc}  {Exe}";
        Assert.Equal(HashAbc, ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_CrLf_ReturnsHash()
    {
        string content = $"SomeHeader\r\n{HashAbc}  {Exe}\r\n";
        Assert.Equal(HashAbc, ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_MultipleBareHashes_ReturnsNull()
    {
        string other = new('2', 64);
        string content = $"{HashAbc}\n{other}";
        Assert.Null(ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_BareHashPlusNamedOther_ReturnsNull()
    {
        string content = $"{HashAbc}\n{new string('3', 64)}  Other.exe";
        Assert.Null(ChecksumVerifier.TryParse(content, Exe));
    }

    [Fact]
    public void TryParse_NameCaseInsensitive_ReturnsHash()
    {
        string content = $"{HashAbc}  {Exe.ToUpperInvariant()}";
        Assert.Equal(HashAbc, ChecksumVerifier.TryParse(content, Exe));
    }

    // ---------- ComputeSha256 ----------

    [Fact]
    public void ComputeSha256_KnownVector()
    {
        Assert.Equal(HashAbc, ChecksumVerifier.ComputeSha256("abc"u8.ToArray()));
    }

    [Fact]
    public void ComputeSha256_RoundTrip_Match()
    {
        byte[] data = "MemoryCleaner-test-payload"u8.ToArray();
        string h = ChecksumVerifier.ComputeSha256(data);
        Assert.True(ChecksumVerifier.IsValidSha256Hex(h));
    }

    // ---------- ConstantTimeEquals ----------

    [Fact]
    public void ConstantTimeEquals_Equal_True()
        => Assert.True(ChecksumVerifier.ConstantTimeEquals(HashAbc, HashAbc));

    [Fact]
    public void ConstantTimeEquals_Different_False()
        => Assert.False(ChecksumVerifier.ConstantTimeEquals(HashAbc, new string('0', 64)));

    [Fact]
    public void ConstantTimeEquals_LengthMismatch_False()
        => Assert.False(ChecksumVerifier.ConstantTimeEquals(HashAbc, "abc"));
}

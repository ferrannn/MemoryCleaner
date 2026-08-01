using System.Security.Cryptography;
using System.Text;

namespace MemoryCleaner.Core;

/// <summary>
/// SHA-256 校验的纯逻辑：解析 sha256sum 文件、计算与比对哈希。
/// 无 I/O、无 HTTP、无 UI，便于单元测试。
///
/// 全部解析走 fail-closed：任何一步拿不到 / 对不上，都返回 null / false，
/// 调用方必须拒绝更新，而不是跳过校验。
/// </summary>
internal static class ChecksumVerifier
{
    /// <summary>是否为 64 位十六进制字符（sha256 摘要的标准表示）。</summary>
    public static bool IsValidSha256Hex(string? s)
        => s != null && s.Length == 64 && s.All(IsHexChar);

    /// <summary>
    /// 解析 sha256sum 格式文件，返回文件名匹配 <paramref name="expectedFileName"/>
    /// 的那一行的 64 位小写哈希；任何异常情况返回 null（fail-closed）。
    ///
    /// 兼容格式：
    ///   "&lt;hash&gt;  name"   —— 文本文件（sha256sum 默认）
    ///   "&lt;hash&gt; *name"   —— 二进制文件（sha256sum -b）
    ///   "&lt;hash&gt;"          —— 裸哈希（无文件名，仅当唯一时接受）
    /// 自动容忍 BOM / CRLF / 大写十六进制 / 多行中混有无关文件。
    /// </summary>
    public static string? TryParse(string? content, string expectedFileName)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // 去 BOM、统一换行
        string normalized = content.TrimStart('﻿').Replace("\r\n", "\n");

        string? bareHash = null;
        int bareCount = 0;
        bool anyNamedLine = false;

        foreach (var raw in normalized.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // 行首必须是 64 位十六进制；否则整行跳过（不匹配的文件行不算失败）
            int i = 0;
            while (i < line.Length && IsHexChar(line[i])) i++;
            if (i != 64) continue;

            string hash = line[..64].ToLowerInvariant();
            string rest = line[64..].TrimStart();

            if (rest.Length == 0)
            {
                bareHash = hash;
                bareCount++;
                continue;
            }

            // 出现命名行 → 本文件采用"按文件名"约定，裸哈希不再可作回退
            anyNamedLine = true;

            // sha256sum 的两种文件名前缀：空格（文本）与星号（二进制）
            rest = rest.TrimStart('*').TrimStart();
            if (string.Equals(rest, expectedFileName, StringComparison.OrdinalIgnoreCase))
                return hash; // 命中目标文件，直接返回
        }

        // 没有任何行点名目标文件时：
        //   - 文件里完全没有命名行 → 仅当存在唯一一个裸哈希才接受
        //   - 存在命名行（哪怕指向别的文件）→ 裸哈希含糊，一律拒绝
        return anyNamedLine ? null : (bareCount == 1 ? bareHash : null);
    }

    /// <summary>计算数据块的 SHA-256，返回小写十六进制。</summary>
    public static string ComputeSha256(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>常量时间比较两个十六进制字符串，防止时序侧信道。</summary>
    public static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
    }

    private static bool IsHexChar(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}

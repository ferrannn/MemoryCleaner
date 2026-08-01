using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace MemoryCleaner.Core;

/// <summary>
/// 通过 GitHub Releases API 检查并下载更新。
/// </summary>
internal static class UpdateChecker
{
    private const string Repo = "ferrannn/MemoryCleaner";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient();
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MemoryCleaner-Updater");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        h.Timeout = TimeSpan.FromSeconds(15);
        return h;
    }

    public sealed record ReleaseInfo(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("assets")] ReleaseAsset[] Assets);

    public sealed record ReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest);

    /// <summary>
    /// 从 GitHub 资产的 digest 字段解析 64 位 SHA-256。
    ///
    /// digest 由 GitHub 服务器在上传时计算并存储（格式 "sha256:&lt;hex&gt;"），
    /// 发布者无法伪造——这是 v1.3.7.1 起的主校验来源。部分 API 镜像不返回该
    /// 字段（digest 为 null）时返回 null，调用方回退到 .sha256 侧车资产。
    /// </summary>
    public static string? ParseDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        // "sha256:xxx" —— 只接受 sha256 算法；其他算法（sha1/md5 等）一律忽略
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string hex = digest[prefix.Length..].Trim();
        return ChecksumVerifier.IsValidSha256Hex(hex) ? hex.ToLowerInvariant() : null;
    }

    public static Version CurrentVersion
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>
    /// 获取最新 Release；全部来源都失败返回 null。
    ///
    /// 先直连 api.github.com，不通再试 API 镜像——否则在 GitHub 不可达的网络里
    /// 连"有没有新版本"都查不到，后面的镜像下载也就无从谈起。
    /// 每个源单独限时，避免几个源都超时时把用户晾在那里。
    /// </summary>
    public static async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        foreach (var template in DownloadMirrors.ApiEndpoints)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(DownloadMirrors.ApiAttemptTimeout);

                var info = await Http.GetFromJsonAsync<ReleaseInfo>(
                    string.Format(template, Repo), timeout.Token);
                if (info != null) return info;
            }
            catch
            {
                // 换下一个源继续试
            }
        }
        return null;
    }

    /// <summary>比较远端版本是否更新。tag 可能带 'v' 前缀。</summary>
    public static bool IsNewer(ReleaseInfo release, out Version remoteVersion)
    {
        var tag = release.TagName.TrimStart('v', 'V');
        if (Version.TryParse(tag, out var v))
        {
            remoteVersion = v;
            return v > CurrentVersion;
        }
        remoteVersion = CurrentVersion;
        return false;
    }

    /// <summary>选择适合当前部署的资产（优先与当前 exe 同名/同类型）。</summary>
    public static ReleaseAsset? PickAsset(ReleaseInfo release)
    {
        if (release.Assets == null || release.Assets.Length == 0) return null;
        string curName = Path.GetFileName(Environment.ProcessPath ?? "MemoryCleaner.exe");
        // 优先同名
        var exact = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, curName, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        // 其次 .exe
        return release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 在 release 里找与 <paramref name="exe"/> 配套的校验文件：
    /// 优先 &lt;exe名&gt;.sha256，其次唯一一个 .sha256 资产；都没有返回 null。
    /// </summary>
    public static ReleaseAsset? PickChecksumAsset(ReleaseInfo release, ReleaseAsset exe)
    {
        if (release.Assets == null || release.Assets.Length == 0) return null;

        var exact = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, exe.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var sha = release.Assets
            .Where(a => a.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return sha.Length == 1 ? sha[0] : null;
    }

    /// <summary>
    /// 解析更新所需的 exe 资产与期望 SHA-256。
    ///
    /// 主路径（v1.3.7.1）：exe 资产自带的 GitHub API digest——由 GitHub 服务器
    /// 在上传时计算，随版本检查的 API 响应一并返回，无需额外请求、发布者无法伪造。
    ///
    /// 兜底：API 镜像不返回 digest 时，回退到配套的 .sha256 侧车资产，由调用方
    /// 通过 <see cref="FetchChecksumAsync"/> 直连 GitHub 拉取内容解析。
    ///
    /// 返回的 ExpectedSha256 为 null 表示两个来源都不可用 → 调用方 fail-closed。
    /// </summary>
    public static async Task<(ReleaseAsset? Exe, string? ExpectedSha256)> PickUpdateAssetsAsync(
        ReleaseInfo release, CancellationToken ct = default)
    {
        var exe = PickAsset(release);
        if (exe == null) return (null, null);

        // 主路径：exe 资产自带的 API digest
        string? expected = ParseDigest(exe.Digest);
        if (expected != null) return (exe, expected);

        // 兜底：侧车资产（若发布者仍上传 .sha256）
        var checksum = PickChecksumAsset(release, exe);
        if (checksum != null)
            expected = await FetchChecksumAsync(checksum, exe.Name, ct);

        return (exe, expected);
    }

    /// <summary>
    /// 直连 GitHub 拉取 .sha256 侧车资产内容并解析出目标 exe 的期望哈希（兜底路径）。
    ///
    /// 信任锚：这里只用 <paramref name="checksumAsset"/>.DownloadUrl 原样直连
    /// （github.com release 资产），绝不套任何加速前缀——被劫持的镜像永远
    /// 喂不进绑定哈希。任何失败返回 null（fail-closed）。
    /// </summary>
    public static async Task<string?> FetchChecksumAsync(
        ReleaseAsset? checksumAsset, string exeAssetName, CancellationToken ct = default)
    {
        if (checksumAsset == null) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            string content = await Http.GetStringAsync(checksumAsset.DownloadUrl, timeout.Token);
            return ChecksumVerifier.TryParse(content, exeAssetName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>更新结果。</summary>
    public readonly record struct UpdateOutcome(bool Success, string? Error)
    {
        public static UpdateOutcome Ok() => new(true, null);
        public static UpdateOutcome Fail(string error) => new(false, error);
    }

    /// <summary>下载进度快照。</summary>
    public readonly record struct DownloadProgress(long Downloaded, long Total, double BytesPerSecond)
    {
        /// <summary>总大小未知时返回 -1。</summary>
        public int Percent => Total > 0 ? (int)(Downloaded * 100 / Total) : -1;
    }

    /// <summary>
    /// 下载资产并用"自替换"方式更新当前 exe：
    /// 新文件落盘为 .new，边下载边算 SHA-256，与 <paramref name="expectedSha256"/>
    /// 比对通过后，用 PowerShell -EncodedCommand 在退出后替换并重启。
    ///
    /// 安全不变量：
    ///   - 完整性校验 fail-closed——入口就要求 <paramref name="expectedSha256"/> 是
    ///     合法 64 位十六进制，下载后哈希不符一律拒绝替换（删 .new）。
    ///   - 自替换脚本不落盘：以 -EncodedCommand 传递固定脚本体，所有路径经
    ///     ProcessStartInfo.Environment 环境变量传入，脚本体内零字符串插值，
    ///     从根上消除 %TEMP% 固定名脚本提权与路径注入。
    /// </summary>
    /// <param name="mirror">下载源；null 表示直连 GitHub。仅加速 exe 载荷，校验和永远直连。</param>
    public static async Task<UpdateOutcome> DownloadAndApplyAsync(
        ReleaseAsset asset,
        string expectedSha256,
        DownloadMirror? mirror = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
            return UpdateOutcome.Fail("无法确定当前程序路径");
        string newPath = currentExe + ".new";

        // 入口即校验：期望哈希不合法直接拒绝，不开始下载
        if (!ChecksumVerifier.IsValidSha256Hex(expectedSha256))
            return UpdateOutcome.Fail("SHA-256 校验信息无效，已拒绝更新");

        try
        {
            string url = (mirror ?? DownloadMirrors.All[0]).Build(asset.DownloadUrl);

            long downloaded = 0;
            using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? asset.Size;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(newPath);

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var buffer = new byte[81920];
                var sw = Stopwatch.StartNew();
                long lastReportBytes = 0;
                var lastReport = TimeSpan.Zero;
                int n;

                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    hash.AppendData(buffer.AsSpan(0, n));
                    downloaded += n;

                    // 限流上报：每 100ms 一次，避免 UI 线程被刷爆
                    var now = sw.Elapsed;
                    if (progress != null && (now - lastReport).TotalMilliseconds >= 100)
                    {
                        double secs = (now - lastReport).TotalSeconds;
                        double bps = secs > 0 ? (downloaded - lastReportBytes) / secs : 0;
                        progress.Report(new DownloadProgress(downloaded, total, bps));
                        lastReport = now;
                        lastReportBytes = downloaded;
                    }
                }
                progress?.Report(new DownloadProgress(downloaded, total, 0));

                string computed = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!ChecksumVerifier.ConstantTimeEquals(computed, expectedSha256))
                {
                    try { File.Delete(newPath); } catch { }
                    return UpdateOutcome.Fail("SHA-256 校验不通过，下载内容可能被篡改或文件损坏，已拒绝更新。建议换一个下载源重试");
                }
            }

            // 大小快速失败：加速源返回错误页时也是 200，这一步能把"下了个 HTML"挡住。
            // 放在哈希校验之后，作为冗余防线（哈希才是权威）。
            if (asset.Size > 0 && downloaded != asset.Size)
            {
                try { File.Delete(newPath); } catch { }
                return UpdateOutcome.Fail($"下载不完整（{downloaded}/{asset.Size} 字节），可能是该下载源不可靠，建议换一个源");
            }

            // 自替换：脚本体固定、路径全部经环境变量传入，避免字符串插值注入。
            // 等当前进程退出 → 备份 → 替换 → 重启。脚本随 -EncodedCommand 走，
            // 不落盘、无需自删。
            //
            // 注意：不要给 Arguments 加 -WindowStyle Hidden——实测在 Win11 的
            // Windows PowerShell 5.1 下，该参数会让 -EncodedCommand 脚本静默不执行
            // （退出码 -1）。隐藏窗口靠 CreateNoWindow + WindowStyle 属性即可。
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {UpdateCommand}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.Environment["MC_PID"] = Environment.ProcessId.ToString();
            psi.Environment["MC_CURRENT_EXE"] = currentExe;
            psi.Environment["MC_NEW_EXE"] = newPath;
            psi.Environment["MC_BACKUP_EXE"] = currentExe + ".bak";

            using var proc = Process.Start(psi);
            if (proc == null)
                return UpdateOutcome.Fail("无法启动更新程序");
            return UpdateOutcome.Ok();
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
            return UpdateOutcome.Fail("已取消");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
            return UpdateOutcome.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 自替换脚本（UTF-16LE Base64 后作为 -EncodedCommand 传入）。
    /// 脚本体是固定常量，不含任何插值；所有路径来自 $env:MC_*。
    /// </summary>
    private static string UpdateCommand
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "try { Wait-Process -Id $env:MC_PID -Timeout 60 -ErrorAction SilentlyContinue } catch {}\n"
          + "Start-Sleep -Milliseconds 500\n"
          + "try {\n"
          + "  Copy-Item -LiteralPath $env:MC_CURRENT_EXE -Destination $env:MC_BACKUP_EXE -Force -ErrorAction SilentlyContinue\n"
          + "  Move-Item -LiteralPath $env:MC_NEW_EXE -Destination $env:MC_CURRENT_EXE -Force\n"
          + "  Start-Process -FilePath $env:MC_CURRENT_EXE\n"
          + "} catch {}\n"));
}

using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
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
        [property: JsonPropertyName("size")] long Size);

    public static Version CurrentVersion
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>获取最新 Release；网络失败返回 null。</summary>
    public static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        try
        {
            return await Http.GetFromJsonAsync<ReleaseInfo>(
                $"https://api.github.com/repos/{Repo}/releases/latest");
        }
        catch
        {
            return null;
        }
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

    /// <summary>更新结果。</summary>
    public readonly record struct UpdateOutcome(bool Success, string? Error)
    {
        public static UpdateOutcome Ok() => new(true, null);
        public static UpdateOutcome Fail(string error) => new(false, error);
    }

    /// <summary>
    /// 下载资产并用"自替换"方式更新当前 exe：
    /// 新文件落盘为 .new，校验大小与预期一致后，写脚本在退出后替换并重启。
    /// </summary>
    public static async Task<UpdateOutcome> DownloadAndApplyAsync(ReleaseAsset asset, IProgress<int>? progress = null)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
            return UpdateOutcome.Fail("无法确定当前程序路径");
        string newPath = currentExe + ".new";

        // 路径安全：拒绝含单引号的路径（会破坏 PS1 字符串插值）
        if (currentExe.Contains('\'') || newPath.Contains('\''))
            return UpdateOutcome.Fail("程序路径含特殊字符，无法自动更新");

        try
        {
            long downloaded = 0;
            using (var resp = await Http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(newPath);
                var buffer = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n));
                    downloaded += n;
                    if (total.HasValue && total > 0)
                        progress?.Report((int)(downloaded * 100 / total.Value));
                }
            }

            // 完整性校验：下载大小必须与 Release 元数据一致
            if (asset.Size > 0 && downloaded != asset.Size)
            {
                try { File.Delete(newPath); } catch { }
                return UpdateOutcome.Fail($"下载不完整（{downloaded}/{asset.Size} 字节），已中止");
            }

            // 生成自替换 PowerShell 脚本：等当前进程退出 → 校验存在 → 备份 → 替换 → 重启 → 自删
            string pid = Environment.ProcessId.ToString();
            string backupPath = currentExe + ".bak";
            string ps1 = Path.Combine(Path.GetTempPath(), "mc_update.ps1");
            await File.WriteAllTextAsync(ps1, $@"
try {{ Wait-Process -Id {pid} -Timeout 60 -ErrorAction SilentlyContinue }} catch {{}}
Start-Sleep -Milliseconds 500
try {{
  Copy-Item -LiteralPath '{currentExe}' -Destination '{backupPath}' -Force -ErrorAction SilentlyContinue
  Move-Item -LiteralPath '{newPath}' -Destination '{currentExe}' -Force
  Start-Process -FilePath '{currentExe}'
}} catch {{}}
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
");

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (proc == null)
                return UpdateOutcome.Fail("无法启动更新程序");
            return UpdateOutcome.Ok();
        }
        catch (Exception ex)
        {
            try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
            return UpdateOutcome.Fail(ex.Message);
        }
    }
}

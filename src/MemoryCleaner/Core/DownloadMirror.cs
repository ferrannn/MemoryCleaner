using System.Diagnostics;

namespace MemoryCleaner.Core;

/// <summary>
/// 下载源。GitHub 在部分网络环境下直连极慢甚至不通，因此提供若干公共加速前缀，
/// 由用户按实测延迟自行选择。
/// </summary>
internal sealed record DownloadMirror(string Name, string Prefix)
{
    /// <summary>把原始 GitHub 链接套上加速前缀；直连源返回原链接。</summary>
    public string Build(string githubUrl)
        => string.IsNullOrEmpty(Prefix) ? githubUrl : Prefix + githubUrl;

    public bool IsDirect => string.IsNullOrEmpty(Prefix);
}

/// <summary>
/// 可用下载源与延迟探测。
///
/// 这些公共加速服务由第三方维护，会新增也会失效，因此探测结果一律以实测为准，
/// 不做任何“哪个更快”的先验假设，探测失败的源在界面上直接标记为不可用。
/// </summary>
internal static class DownloadMirrors
{
    // 均为实测可用（对 release 文件发 Range 请求能拿到 206）。
    // 加速服务时有兴废，界面会按实测延迟排序并把不可用的标出来，
    // 所以这里失效一两个也不影响使用。
    //
    // 安全不变量：这些前缀只加速 exe 载荷的下载，绝不能被用于拉取 .sha256
    // 校验文件。绑定哈希必须来自 GitHub 直连（见 UpdateChecker.FetchChecksumAsync
    // 从不调用 Build）。镜像源只提供速度，永远不是信任边界。
    public static readonly DownloadMirror[] All =
    {
        new("GitHub 直连", ""),
        new("gh-proxy.com", "https://gh-proxy.com/"),
        new("ghfast.top", "https://ghfast.top/"),
        new("ghproxy.net", "https://ghproxy.net/"),
        new("gh.llkk.cc", "https://gh.llkk.cc/"),
    };

    /// <summary>探测失败（超时 / 拒绝 / 返回错误码）时的延迟值。</summary>
    public const int Unreachable = -1;

    /// <summary>
    /// Release API 的可用地址，按优先级排列。
    ///
    /// 注意：上面那些加速前缀只代理 github.com 上的文件，套到 api.github.com 上
    /// 一律 403（已实测），所以 API 必须用另一组专门的镜像，不能复用文件源。
    /// </summary>
    public static readonly string[] ApiEndpoints =
    {
        "https://api.github.com/repos/{0}/releases/latest",
        "https://gh-api.p3terx.com/repos/{0}/releases/latest",
    };

    /// <summary>单次 API 尝试的超时。逐个源串行重试，总耗时须控制在用户可忍受的范围。</summary>
    public static readonly TimeSpan ApiAttemptTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Probe = CreateProbeClient();

    private static HttpClient CreateProbeClient()
    {
        var h = new HttpClient(new SocketsHttpHandler
        {
            // 探测只关心首字节，不需要自动解压或连接复用带来的干扰
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        });
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MemoryCleaner-Updater");
        h.Timeout = TimeSpan.FromSeconds(6);
        return h;
    }

    /// <summary>
    /// 测量某个源的可用延迟：对真实下载链接请求首个字节，计时到响应头返回。
    ///
    /// 之所以不用 ping 或访问站点首页——有的加速服务站点可达但并不代理 release
    /// 文件，那种“通”对用户毫无意义。只有真的能取到这个文件才算可用。
    /// </summary>
    /// <returns>毫秒；不可用返回 <see cref="Unreachable"/>。</returns>
    public static async Task<int> MeasureAsync(DownloadMirror mirror, string githubUrl, CancellationToken ct = default)
    {
        try
        {
            var url = mirror.Build(githubUrl);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0); // 只要 1 字节

            var sw = Stopwatch.StartNew();
            using var resp = await Probe.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            return resp.IsSuccessStatusCode ? (int)sw.ElapsedMilliseconds : Unreachable;
        }
        catch
        {
            return Unreachable;
        }
    }
}

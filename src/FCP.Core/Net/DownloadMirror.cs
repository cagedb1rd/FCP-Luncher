using System.Diagnostics;
using FCP.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FCP.Core.Net;

/// <summary>官方下载地址 ↔ BMCLAPI 镜像地址的对应关系。</summary>
public static class MirrorMap
{
    public const string MirrorRoot = "https://bmclapi2.bangbang93.com/";

    private static readonly (string Official, string Mirror)[] Prefixes =
    [
        ("https://piston-meta.mojang.com/", MirrorRoot),
        ("https://launchermeta.mojang.com/", MirrorRoot),
        ("https://piston-data.mojang.com/", MirrorRoot),
        ("https://launcher.mojang.com/", MirrorRoot),
        ("https://resources.download.minecraft.net/", MirrorRoot + "assets/"),
        ("https://libraries.minecraft.net/", MirrorRoot + "maven/"),
        ("https://maven.minecraftforge.net/", MirrorRoot + "maven/"),
        ("https://files.minecraftforge.net/maven/", MirrorRoot + "maven/"),
        ("https://maven.neoforged.net/releases/", MirrorRoot + "maven/"),
        ("https://maven.fabricmc.net/", MirrorRoot + "maven/"),
        ("https://meta.fabricmc.net/", MirrorRoot + "fabric-meta/"),
        ("https://maven.quiltmc.org/repository/release/", MirrorRoot + "maven/"),
        ("https://meta.quiltmc.org/", MirrorRoot + "quilt-meta/"),
    ];

    /// <summary>返回对应的镜像地址；不在镜像范围内时返回 null。</summary>
    public static Uri? ToMirror(Uri official)
    {
        if (official.Scheme != Uri.UriSchemeHttps && official.Scheme != Uri.UriSchemeHttp)
            return null;
        // 老版本的资源索引中是 http 地址
        var text = official.Scheme == Uri.UriSchemeHttp ? "https" + official.AbsoluteUri[4..] : official.AbsoluteUri;
        foreach (var (prefix, mirror) in Prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return new Uri(mirror + text[prefix.Length..]);
        }
        return null;
    }
}

/// <summary>
/// 下载源选择与自动回退（docs/04 §7）：
/// <list type="bullet">
/// <item>“自动”：第一次下载前分别向官方源和镜像源请求同一个小文件，先用更快的那个；</item>
/// <item>无论哪种设置，首选源失败（连接失败、超时、服务器错误、文件不存在）时自动换另一个源重试；</item>
/// <item>首选源失败过之后，本次运行中优先使用另一个源，避免每个文件都先等一次超时。</item>
/// </list>
/// </summary>
public sealed class MirrorHandler(SettingsStore settings, ILogger<MirrorHandler> logger) : DelegatingHandler
{
    /// <summary>有备用源时，首选源等待响应头的最长时间。</summary>
    public TimeSpan PrimaryHeaderTimeout { get; init; } = TimeSpan.FromSeconds(15);

    internal const string ProbeOfficial = "https://libraries.minecraft.net/com/mojang/brigadier/1.0.18/brigadier-1.0.18.jar";

    private readonly Lock _lock = new();
    private Task<bool>? _probe;
    private bool? _stickyPreferMirror;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is not { } official || MirrorMap.ToMirror(official) is not { } mirror)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var preferMirror = await PreferMirrorAsync(cancellationToken).ConfigureAwait(false);
        var (first, second) = preferMirror ? (mirror, official) : (official, mirror);

        HttpResponseMessage? response = null;
        Exception? error = null;
        using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            attempt.CancelAfter(PrimaryHeaderTimeout);
            try
            {
                response = await base.SendAsync(Clone(request, first), attempt.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                error = ex;
            }
        }

        logger.LogInformation("{Source}下载失败（{Reason}），改用{Other}：{Url}",
            preferMirror ? "镜像源" : "官方源", response is null ? error?.GetType().Name : $"HTTP {(int)response.StatusCode}", preferMirror ? "官方源" : "镜像源", first);
        lock (_lock)
            _stickyPreferMirror = !preferMirror;
        try
        {
            var fallback = await base.SendAsync(Clone(request, second), cancellationToken).ConfigureAwait(false);
            if (fallback.IsSuccessStatusCode || response is null)
            {
                response?.Dispose();
                return fallback;
            }
            fallback.Dispose();
            return response;
        }
        catch (Exception) when (response is not null)
        {
            // 备用源也失败：返回首选源的错误响应，由调用方处理
            return response;
        }
    }

    private async Task<bool> PreferMirrorAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_stickyPreferMirror is { } sticky)
                return sticky;
        }
        var source = (await settings.LoadAsync(cancellationToken).ConfigureAwait(false)).DownloadSource;
        if (source != DownloadSource.Auto)
            return source == DownloadSource.Mirror;
        Task<bool> probe;
        lock (_lock)
            probe = _probe ??= ProbeAsync();
        return await probe.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>同时下载官方源和镜像源上的同一个小文件，比较用时。官方源只要不明显更慢就优先使用官方源。</summary>
    private async Task<bool> ProbeAsync()
    {
        async Task<TimeSpan?> MeasureAsync(Uri uri)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await base.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;
                await response.Content.CopyToAsync(Stream.Null, timeout.Token).ConfigureAwait(false);
                return stopwatch.Elapsed;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                return null;
            }
        }

        var officialUri = new Uri(ProbeOfficial);
        var officialTask = MeasureAsync(officialUri);
        var mirrorTask = MeasureAsync(MirrorMap.ToMirror(officialUri)!);
        var official = await officialTask.ConfigureAwait(false);
        // 镜像源只有在用时不到官方源一半时才会被选中：官方源完成时镜像源还没完成，就不必再等它（避免慢的源拖住第一次下载）
        var mirror = official is null || mirrorTask.IsCompleted ? await mirrorTask.ConfigureAwait(false) : null;
        var preferMirror = official is null
            ? mirror is not null
            : mirror is { } m && official.Value > m * 2 && official.Value > TimeSpan.FromSeconds(1);
        logger.LogInformation("下载源测速：官方源 {Official}，镜像源 {Mirror}，优先使用{Choice}",
            official is { } o ? $"{o.TotalMilliseconds:F0} ms" : "失败",
            mirror is { } mm ? $"{mm.TotalMilliseconds:F0} ms" : mirrorTask.IsCompleted ? "失败" : "较慢（未等待）",
            preferMirror ? "镜像源" : "官方源");
        return preferMirror;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request, Uri uri)
    {
        var clone = new HttpRequestMessage(request.Method, uri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options)
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        return clone;
    }
}

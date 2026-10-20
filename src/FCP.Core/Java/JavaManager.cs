using FCP.Core.Storage;
using Microsoft.Extensions.Logging;

namespace FCP.Core.Java;

/// <summary>java.json：探测结果缓存 + 用户手动添加 / 移除的 Java。</summary>
public sealed class JavaSettings
{
    public List<CachedJava> Cache { get; set; } = [];
    public List<string> UserAdded { get; set; } = [];
    public List<string> Removed { get; set; } = [];
}

public sealed record CachedJava(string JavaExecutable, string Version, string? Vendor, bool Is64Bit, JavaSource Source, DateTime LastWriteTimeUtc);

/// <summary>
/// Java 列表的统一入口：发现 → 探测（按“路径 + 修改时间”缓存）→ 合并用户的添加与移除。
/// 每次打开启动器在后台调用 <see cref="RefreshAsync"/>，新安装的 Java 能立即被发现（PCL 的 Java 列表不会自动刷新）。
/// </summary>
public sealed class JavaManager(JavaDiscovery discovery, JavaProbe probe, AppPaths paths, ILogger<JavaManager> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<JavaRuntime> _current = [];

    public IReadOnlyList<JavaRuntime> Current => _current;

    /// <summary>重新扫描完成后触发，参数为新发现的 Java。</summary>
    public event EventHandler<IReadOnlyList<JavaRuntime>>? Refreshed;

    public async Task<IReadOnlyList<JavaRuntime>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await JsonFile.ReadAsync<JavaSettings>(paths.JavaFile, cancellationToken).ConfigureAwait(false) ?? new JavaSettings();
            var candidates = discovery.FindCandidates([paths.RuntimeDirectory]).ToList();
            foreach (var user in settings.UserAdded.Where(File.Exists))
            {
                if (!candidates.Any(c => SamePath(c.JavaExe, user)))
                    candidates.Add((user, JavaSource.User));
            }
            candidates.RemoveAll(c => settings.Removed.Any(r => SamePath(r, c.JavaExe)));

            var runtimes = new List<JavaRuntime>();
            var newCache = new List<CachedJava>();
            await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (candidate, ct) =>
            {
                var writeTime = File.GetLastWriteTimeUtc(candidate.JavaExe);
                var cached = settings.Cache.FirstOrDefault(c => SamePath(c.JavaExecutable, candidate.JavaExe) && c.LastWriteTimeUtc == writeTime);
                JavaRuntime? runtime = cached is not null && Version.TryParse(cached.Version, out var version)
                    ? new JavaRuntime { JavaExecutable = candidate.JavaExe, Version = version, Vendor = cached.Vendor, Is64Bit = cached.Is64Bit, Source = candidate.Source }
                    : await probe.ProbeAsync(candidate.JavaExe, candidate.Source, ct).ConfigureAwait(false);
                if (runtime is null)
                    return;
                lock (runtimes)
                {
                    runtimes.Add(runtime);
                    newCache.Add(new CachedJava(runtime.JavaExecutable, runtime.Version.ToString(), runtime.Vendor, runtime.Is64Bit, runtime.Source, writeTime));
                }
            }).ConfigureAwait(false);

            var sorted = runtimes.OrderByDescending(r => r.Major).ThenByDescending(r => r.Version).ToList();
            // “新发现”是相对上一次保存的列表而言（首次运行时缓存为空，不算新发现）
            var added = settings.Cache.Count == 0
                ? []
                : sorted.Where(r => !settings.Cache.Any(c => SamePath(c.JavaExecutable, r.JavaExecutable))).ToList();
            _current = sorted;
            settings.Cache = newCache;
            await JsonFile.WriteAsync(paths.JavaFile, settings, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Java 列表：{List}", string.Join("；", sorted.Select(r => $"{r.DisplayName} [{r.Source}] {r.JavaExecutable}")));
            Refreshed?.Invoke(this, added);
            return sorted;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>手动添加 java.exe；返回探测结果（无效时为 null）。</summary>
    public async Task<JavaRuntime?> AddUserJavaAsync(string javaExe, CancellationToken cancellationToken = default)
    {
        var runtime = await probe.ProbeAsync(javaExe, JavaSource.User, cancellationToken).ConfigureAwait(false);
        if (runtime is null)
            return null;
        var settings = await JsonFile.ReadAsync<JavaSettings>(paths.JavaFile, cancellationToken).ConfigureAwait(false) ?? new JavaSettings();
        settings.UserAdded.RemoveAll(p => SamePath(p, javaExe));
        settings.UserAdded.Add(javaExe);
        settings.Removed.RemoveAll(p => SamePath(p, javaExe));
        await JsonFile.WriteAsync(paths.JavaFile, settings, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return runtime;
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

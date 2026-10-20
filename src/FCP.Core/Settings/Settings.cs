using FCP.Core.Storage;

namespace FCP.Core.Settings;

/// <summary>默认版本隔离策略（与 PCL 的 5 档对应，docs/02 §2）。</summary>
public enum IsolationPolicy
{
    Off,
    Modded,
    NonRelease,
    ModdedAndNonRelease,
    All,
}

public enum MemoryMode
{
    Auto,
    Custom,
}

/// <summary>游戏启动后启动器的行为。</summary>
public enum LauncherVisibility
{
    KeepOpen,
    Minimize,
    HideAndRestore,
    HideAndExit,
    Exit,
}

public enum GcPolicy
{
    /// <summary>官方启动器默认的 G1 参数。</summary>
    G1,
    /// <summary>Java 21+ 使用分代 ZGC，否则 G1。</summary>
    GenerationalZgc,
    /// <summary>不添加，由用户在 JVM 参数中自行指定。</summary>
    None,
}

/// <summary>游戏进程优先级（与 PCL 的“高 / 中 / 低”对应）。</summary>
public enum GamePriority
{
    Normal,
    High,
    Low,
}

/// <summary>从 PCL 导入全局设置的记录（只导入一次；之后在 FCP 中的修改优先）。</summary>
public sealed record PclImportRecord(DateTimeOffset ImportedAt, string SourceDirectory, IReadOnlyList<string> Items)
{
    /// <summary>需要用户确认或处理的事项（例如需要登录皮肤站的版本）。</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public enum JavaMode
{
    /// <summary>根据版本需求自动选择，没有合适的就下载。</summary>
    Auto,
    /// <summary>使用指定的 java.exe。</summary>
    Specific,
}

public sealed record MemorySetting(MemoryMode Mode, int CustomMb);

public sealed record WindowSetting(bool FullScreen, int? Width, int? Height);

/// <summary>全局设置（settings.json）。</summary>
/// <summary>下载源（docs/04 §7）。</summary>
public enum DownloadSource
{
    /// <summary>测速后自动选择，失败时自动换源。</summary>
    Auto,
    Official,
    Mirror,
}

public sealed class GlobalSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>大文件数据目录（D-014）；为空时首次运行自动确定。</summary>
    public string? DataDirectory { get; set; }

    public IsolationPolicy Isolation { get; set; } = IsolationPolicy.ModdedAndNonRelease;
    public MemorySetting Memory { get; set; } = new(MemoryMode.Auto, 4096);
    public LauncherVisibility Visibility { get; set; } = LauncherVisibility.HideAndRestore;
    public WindowSetting Window { get; set; } = new(false, 854, 480);
    public GcPolicy Gc { get; set; } = GcPolicy.G1;
    public string ExtraJvmArguments { get; set; } = "";
    public string ExtraGameArguments { get; set; } = "";

    /// <summary>显示在游戏主界面左下角的文字（--versionType）。</summary>
    public string VersionTypeText { get; set; } = "FCP";

    public bool AutoChineseLanguage { get; set; } = true;

    public DownloadSource DownloadSource { get; set; } = DownloadSource.Auto;

    public GamePriority Priority { get; set; } = GamePriority.Normal;

    /// <summary>从 PCL 导入全局设置的记录；为 null 表示还没有导入过。</summary>
    public PclImportRecord? PclImport { get; set; }

    /// <summary>当前账号 id。</summary>
    public string? SelectedAccountId { get; set; }

    /// <summary>首次运行（迁移向导）是否已完成。</summary>
    public bool FirstRunCompleted { get; set; }
}

/// <summary>版本独立设置：每一项为 null 表示“跟随全局设置”。</summary>
public sealed class InstanceSettings
{
    public bool? Isolated { get; set; }
    public MemorySetting? Memory { get; set; }
    public JavaMode? JavaMode { get; set; }
    public string? JavaPath { get; set; }
    public GcPolicy? Gc { get; set; }
    public string? ExtraJvmArguments { get; set; }
    public string? ExtraGameArguments { get; set; }
    public string? AutoJoinServer { get; set; }
    public WindowSetting? Window { get; set; }
}

/// <summary>settings.json 的读写。</summary>
public sealed class SettingsStore(AppPaths paths)
{
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task<GlobalSettings>? _loading;

    /// <summary>
    /// 所有调用方拿到的是同一个对象（并发的第一次读取也只读一次文件），
    /// 否则不同页面各改各的副本，后保存的会覆盖先保存的修改。
    /// </summary>
    public Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
            _loading ??= LoadCoreAsync();
        return _loading.WaitAsync(cancellationToken);
    }

    private async Task<GlobalSettings> LoadCoreAsync() =>
        await JsonFile.ReadAsync<GlobalSettings>(paths.SettingsFile, CancellationToken.None).ConfigureAwait(false) ?? new GlobalSettings();

    public async Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        lock (_lock)
            _loading = Task.FromResult(settings);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await JsonFile.WriteAsync(paths.SettingsFile, settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

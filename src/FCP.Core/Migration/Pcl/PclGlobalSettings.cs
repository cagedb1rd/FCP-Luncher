using FCP.Core.Platform;
using FCP.Core.Settings;

namespace FCP.Core.Migration.Pcl;

/// <summary>
/// PCL 全局设置中可以迁移的明文项（docs/02 §2）。没有保存过的项取 PCL 的默认值：
/// 迁移的目标是“行为和在 PCL 中一样”，而 PCL 用户习惯的正是这些默认行为。
/// </summary>
public sealed record PclGlobalSettings
{
    public const string DefaultJvmArguments = "-XX:-OmitStackTraceInFastThrow -Djdk.lang.Process.allowAmbiguousCommands=True -Dfml.ignoreInvalidMinecraftCertificates=True -Dfml.ignorePatchDiscrepancies=True";

    /// <summary>读取的 PCL 目录（PCL.exe 所在目录）。</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>LaunchRamType：0=自动，1=自定义。</summary>
    public int RamType { get; init; }

    /// <summary>LaunchRamCustom：滑块刻度值（docs/02 §4）。</summary>
    public int RamCustomScale { get; init; } = 15;

    /// <summary>LaunchArgumentIndieV2：0=关闭 … 4=全部。</summary>
    public int IsolationLevel { get; init; } = 4;

    /// <summary>LaunchArgumentVisible：0=立即关闭，2=隐藏并在游戏退出后关闭，3=隐藏并在游戏退出后重新打开，4=最小化，5=保持不变。</summary>
    public int Visibility { get; init; } = 5;

    /// <summary>LaunchArgumentPriority：0=高，1=中，2=低。</summary>
    public int Priority { get; init; } = 1;

    /// <summary>LaunchArgumentWindowType：0=全屏，1=默认，2=与启动器一致，3=自定义，4=最大化。</summary>
    public int WindowType { get; init; } = 1;
    public int WindowWidth { get; init; } = 854;
    public int WindowHeight { get; init; } = 480;

    public string JvmArguments { get; init; } = DefaultJvmArguments;
    public string GameArguments { get; init; } = "";

    /// <summary>LaunchAdvanceGC：0=尽量 ZGC，1=尽量分代 ZGC，2=标准 G1，3=不指定，4=调优 G1。</summary>
    public int Gc { get; init; } = 4;

    /// <summary>LaunchArgumentInfo（--versionType）。</summary>
    public string VersionInfo { get; init; } = "PCL";

    /// <summary>ToolDownloadSource：0=尽量镜像，1=官方优先、慢时切镜像，2=尽量官方。</summary>
    public int DownloadSource { get; init; } = 1;
}

public static class PclGlobalSettingsReader
{
    private static readonly string[] RegistryKeys = [@"Software\PCL", @"Software\PCLDebug"];

    /// <summary>读取 PCL 的全局设置（只读）。PCL 目录中没有 <c>PCL\Setup.ini</c> 时返回 null。</summary>
    public static async Task<PclGlobalSettings?> ReadAsync(string pclDirectory, IRegistryReader registry, CancellationToken cancellationToken = default)
    {
        var setupPath = Path.Combine(pclDirectory, "PCL", "Setup.ini");
        if (!File.Exists(setupPath))
            return null;
        var ini = await PclIniFile.LoadAsync(setupPath, cancellationToken).ConfigureAwait(false);
        int? Reg(string name)
        {
            foreach (var key in RegistryKeys)
            {
                if (registry.ReadString(RegistryHive.CurrentUser, key, name) is { } text && int.TryParse(text, out var value))
                    return value;
            }
            return null;
        }

        var defaults = new PclGlobalSettings { SourceDirectory = pclDirectory };
        return defaults with
        {
            RamType = ini.GetInt("LaunchRamType") ?? defaults.RamType,
            RamCustomScale = ini.GetInt("LaunchRamCustom") ?? defaults.RamCustomScale,
            IsolationLevel = ini.GetInt("LaunchArgumentIndieV2") ?? defaults.IsolationLevel,
            Visibility = Reg("LaunchArgumentVisible") ?? defaults.Visibility,
            Priority = Reg("LaunchArgumentPriority") ?? defaults.Priority,
            WindowType = ini.GetInt("LaunchArgumentWindowType") ?? defaults.WindowType,
            WindowWidth = ini.GetInt("LaunchArgumentWindowWidth") ?? defaults.WindowWidth,
            WindowHeight = ini.GetInt("LaunchArgumentWindowHeight") ?? defaults.WindowHeight,
            JvmArguments = ini.GetString("LaunchAdvanceJvm") ?? defaults.JvmArguments,
            GameArguments = ini.GetString("LaunchAdvanceGame") ?? defaults.GameArguments,
            Gc = ini.GetInt("LaunchAdvanceGC") ?? defaults.Gc,
            VersionInfo = ini.GetString("LaunchArgumentInfo") ?? defaults.VersionInfo,
            DownloadSource = Reg("ToolDownloadSource") ?? defaults.DownloadSource,
        };
    }
}

/// <summary>把 PCL 的全局设置映射为 FCP 的全局设置（docs/06 §3.3）。</summary>
public static class PclGlobalSettingsMapper
{
    /// <summary>写入 <paramref name="target"/>，返回给用户看的导入清单。</summary>
    public static IReadOnlyList<string> Apply(PclGlobalSettings pcl, GlobalSettings target)
    {
        var items = new List<string>();

        if (pcl.RamType == 1)
        {
            var mb = PclRamScale.ToMegabytes(pcl.RamCustomScale);
            target.Memory = new MemorySetting(MemoryMode.Custom, mb);
            items.Add($"游戏内存：自定义 {mb / 1024.0:0.#} GB");
        }
        else
        {
            target.Memory = target.Memory with { Mode = MemoryMode.Auto };
            items.Add("游戏内存：自动配置");
        }

        target.Isolation = pcl.IsolationLevel switch
        {
            0 => IsolationPolicy.Off,
            1 => IsolationPolicy.Modded,
            2 => IsolationPolicy.NonRelease,
            3 => IsolationPolicy.ModdedAndNonRelease,
            _ => IsolationPolicy.All,
        };
        items.Add("默认版本隔离：" + target.Isolation switch
        {
            IsolationPolicy.Off => "关闭",
            IsolationPolicy.Modded => "隔离可安装 Mod 的版本",
            IsolationPolicy.NonRelease => "隔离非正式版",
            IsolationPolicy.ModdedAndNonRelease => "隔离可安装 Mod 的版本与非正式版",
            _ => "隔离所有版本",
        });

        target.Visibility = pcl.Visibility switch
        {
            0 => LauncherVisibility.Exit,
            2 => LauncherVisibility.HideAndExit,
            3 => LauncherVisibility.HideAndRestore,
            4 => LauncherVisibility.Minimize,
            _ => LauncherVisibility.KeepOpen,
        };
        items.Add("游戏启动后启动器：" + target.Visibility switch
        {
            LauncherVisibility.Exit => "关闭",
            LauncherVisibility.HideAndExit => "隐藏，游戏退出后关闭",
            LauncherVisibility.HideAndRestore => "隐藏，游戏退出后回来",
            LauncherVisibility.Minimize => "最小化",
            _ => "保持不变",
        });

        target.Priority = pcl.Priority switch
        {
            0 => GamePriority.High,
            2 => GamePriority.Low,
            _ => GamePriority.Normal,
        };
        items.Add("游戏进程优先级：" + target.Priority switch { GamePriority.High => "高", GamePriority.Low => "低", _ => "中" });

        target.Window = pcl.WindowType switch
        {
            0 => new WindowSetting(true, null, null),
            3 when pcl.WindowWidth > 0 && pcl.WindowHeight > 0 => new WindowSetting(false, pcl.WindowWidth, pcl.WindowHeight),
            _ => new WindowSetting(false, 854, 480),
        };
        if (pcl.WindowType is 0 or 3)
            items.Add(target.Window.FullScreen ? "游戏窗口：全屏" : $"游戏窗口：{target.Window.Width} × {target.Window.Height}");

        target.Gc = MapGc(pcl.Gc);

        // PCL 默认的 JVM 参数我们已经全部内置，只有改过才导入
        if (!string.IsNullOrWhiteSpace(pcl.JvmArguments) && pcl.JvmArguments.Trim() != PclGlobalSettings.DefaultJvmArguments)
        {
            target.ExtraJvmArguments = pcl.JvmArguments.Trim();
            items.Add("自定义 JVM 参数");
        }
        if (!string.IsNullOrWhiteSpace(pcl.GameArguments))
        {
            target.ExtraGameArguments = pcl.GameArguments.Trim();
            items.Add("自定义游戏参数");
        }
        // “PCL” 是 PCL 的默认值，换成我们自己的；自定义的文字沿用
        if (!string.IsNullOrWhiteSpace(pcl.VersionInfo) && pcl.VersionInfo != "PCL")
        {
            target.VersionTypeText = pcl.VersionInfo;
            items.Add($"游戏内显示的版本信息：{pcl.VersionInfo}");
        }

        target.DownloadSource = pcl.DownloadSource switch
        {
            0 => DownloadSource.Mirror,
            2 => DownloadSource.Official,
            _ => DownloadSource.Auto,
        };
        return items;
    }

    /// <summary>PCL 的 GC 选项：0=尽量 ZGC，1=尽量分代 ZGC，2=标准 G1，3=不指定，4=调优 G1。</summary>
    public static GcPolicy MapGc(int pclValue) => pclValue switch
    {
        0 or 1 => GcPolicy.GenerationalZgc,
        3 => GcPolicy.None,
        _ => GcPolicy.G1,
    };
}

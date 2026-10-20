namespace FCP.Core;

/// <summary>
/// 启动器自身数据的位置（docs/04 §6.1，D-014）。
/// <list type="bullet">
/// <item><see cref="ConfigDirectory"/>：小配置（设置、账号、文件夹列表、日志），默认 %APPDATA%\FCP。</item>
/// <item><see cref="DataDirectory"/>：大文件（Java 运行时、缓存、备份），默认尽量不放在系统盘。</item>
/// <item>便携模式：exe 旁边有 <c>fcp.portable</c> 时，全部放在 <c>&lt;exe&gt;\FCP-data</c>。</item>
/// <item>开发与测试：环境变量 FCP_CONFIG_DIR / FCP_DATA_DIR 可以覆盖。</item>
/// </list>
/// </summary>
public sealed record AppPaths(string ConfigDirectory, string DataDirectory, string ExecutableDirectory, bool IsPortable)
{
    public const string PortableMarker = "fcp.portable";
    public const string PortableDataFolder = "FCP-data";

    public string LogsDirectory => Path.Combine(ConfigDirectory, "logs");
    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");
    public string FoldersFile => Path.Combine(ConfigDirectory, "folders.json");
    public string AccountsFile => Path.Combine(ConfigDirectory, "accounts.json");
    public string JavaFile => Path.Combine(ConfigDirectory, "java.json");
    public string RuntimeDirectory => Path.Combine(DataDirectory, "runtime");
    public string CacheDirectory => Path.Combine(DataDirectory, "cache");
    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    /// <param name="executableDirectory">启动器 exe 所在目录。</param>
    /// <param name="savedDataDirectory">设置中保存的数据目录（用户更改过或首次运行时确定的），可以为空。</param>
    /// <param name="systemDrive">系统盘根目录，例如 C:\。</param>
    public static AppPaths Resolve(string executableDirectory, string? savedDataDirectory = null, string? systemDrive = null)
    {
        var exeDir = Path.GetFullPath(executableDirectory);
        if (File.Exists(Path.Combine(exeDir, PortableMarker)))
        {
            var portable = Path.Combine(exeDir, PortableDataFolder);
            return new AppPaths(portable, portable, exeDir, true);
        }

        var config = Environment.GetEnvironmentVariable("FCP_CONFIG_DIR") is { Length: > 0 } c
            ? c
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FCP");
        var data = Environment.GetEnvironmentVariable("FCP_DATA_DIR") is { Length: > 0 } d
            ? d
            : savedDataDirectory ?? DefaultDataDirectory(exeDir, null, systemDrive);
        return new AppPaths(Path.GetFullPath(config), Path.GetFullPath(data), exeDir, false);
    }

    /// <summary>
    /// 首次运行时的默认数据目录：exe 不在系统盘 → exe 旁边；否则游戏文件夹不在系统盘 → 与 .minecraft 同级；
    /// 都不满足 → %LOCALAPPDATA%\FCP。
    /// </summary>
    public static string DefaultDataDirectory(string executableDirectory, string? firstGameFolder, string? systemDrive = null)
    {
        systemDrive ??= Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
        bool OnSystemDrive(string path) =>
            string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), systemDrive, StringComparison.OrdinalIgnoreCase);

        if (!OnSystemDrive(executableDirectory))
            return Path.Combine(executableDirectory, PortableDataFolder);
        if (firstGameFolder is not null && !OnSystemDrive(firstGameFolder) && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(firstGameFolder)) is { } parent)
            return Path.Combine(parent, PortableDataFolder);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FCP");
    }
}

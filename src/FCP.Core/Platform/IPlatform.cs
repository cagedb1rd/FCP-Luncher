namespace FCP.Core.Platform;

/// <summary>读取注册表（Core 不直接依赖 Windows API，由 FCP.Platform.Windows 实现）。</summary>
public interface IRegistryReader
{
    /// <summary>读取字符串值，例如 <c>ReadString(RegistryHive.CurrentUser, @"Software\PCL", "LaunchFolders")</c>；不存在时返回 null。</summary>
    string? ReadString(RegistryHive hive, string subKey, string valueName, bool use32BitView = false);

    /// <summary>列出子键名称；键不存在时返回空。</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKey, bool use32BitView = false);
}

public enum RegistryHive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>Shell 相关：解析快捷方式、打开文件夹等。</summary>
public interface IShell
{
    /// <summary>解析 .lnk 快捷方式的目标路径，失败时返回 null。</summary>
    string? ResolveShortcut(string lnkPath);

    void OpenFolder(string path);

    /// <summary>创建桌面快捷方式。</summary>
    void CreateShortcut(string lnkPath, string targetPath, string arguments, string? workingDirectory, string? description);
}

/// <summary>查找进程的窗口。</summary>
public interface IWindowLocator
{
    /// <summary>进程是否已经有可见的顶层窗口（用于判断游戏窗口是否出现）。</summary>
    bool HasVisibleWindow(int processId);
}

/// <summary>系统内存信息。</summary>
public interface ISystemInfo
{
    /// <summary>物理内存总量（MB）。</summary>
    long TotalPhysicalMemoryMb { get; }

    /// <summary>当前可用物理内存（MB）。</summary>
    long AvailablePhysicalMemoryMb { get; }
}

/// <summary>对令牌等敏感数据加密（Windows 上为 DPAPI，当前用户范围）。</summary>
public interface IProtectedData
{
    string Protect(string plainText);
    string? Unprotect(string protectedText);
}

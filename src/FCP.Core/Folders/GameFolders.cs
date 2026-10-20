using FCP.Core.Migration.Pcl;
using FCP.Core.Platform;
using FCP.Core.Storage;
using Microsoft.Extensions.Logging;

namespace FCP.Core.Folders;

public enum GameFolderSource
{
    /// <summary>启动器 exe 所在目录（便携包）。</summary>
    Portable,
    /// <summary>官方启动器的 %APPDATA%\.minecraft。</summary>
    Official,
    /// <summary>来自 PCL（或 PCL 社区版）的记录。</summary>
    Pcl,
    /// <summary>用户手动添加。</summary>
    User,
}

/// <summary>一个游戏文件夹（.minecraft 根目录）。</summary>
public sealed record GameFolder(string Path, string DisplayName, GameFolderSource Source)
{
    public string VersionsDirectory => System.IO.Path.Combine(Path, "versions");
}

/// <summary>folders.json：用户添加、重命名、移出的文件夹，以及上次的选择。</summary>
public sealed class FolderSettings
{
    public List<FolderEntry> Folders { get; set; } = [];
    public string? SelectedFolder { get; set; }

    /// <summary>每个文件夹上次选中的版本：文件夹路径 → 版本名。</summary>
    public Dictionary<string, string> SelectedInstances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FolderEntry
{
    public required string Path { get; set; }
    public string? Name { get; set; }

    /// <summary>用户把自动发现的文件夹“移出列表”。</summary>
    public bool IsRemoved { get; set; }
}

/// <summary>发现结果：文件夹列表 + 从 PCL 读到的选择线索。</summary>
public sealed record GameFolderDiscoveryResult(
    IReadOnlyList<GameFolder> Folders,
    string? PclSelectedFolder,
    IReadOnlyList<string> PclExecutableDirectories);

/// <summary>
/// 发现电脑上的游戏文件夹。对 PCL 的数据只读（docs/06）。
/// </summary>
public sealed class GameFolderDiscovery(IRegistryReader registry, IShell shell, ILogger<GameFolderDiscovery> logger)
{
    private static readonly string[] PclRegistryKeys = [@"Software\PCL", @"Software\PCLDebug"];

    public async Task<GameFolderDiscoveryResult> DiscoverAsync(string executableDirectory, FolderSettings settings, CancellationToken cancellationToken = default)
    {
        var found = new List<GameFolder>();
        void Add(string path, string name, GameFolderSource source)
        {
            var normalized = Normalize(path);
            if (found.Any(f => SamePath(f.Path, normalized)))
                return;
            found.Add(new GameFolder(normalized, name, source));
        }

        // 1. 启动器所在目录（便携包：exe 与 .minecraft 放在一起）
        foreach (var folder in FoldersNear(executableDirectory))
            Add(folder, "当前文件夹", GameFolderSource.Portable);

        // 2. PCL：注册表中的自定义文件夹 + PCL.exe 所在目录
        var pclExeDirs = FindPclExecutableDirectories(executableDirectory);
        string? pclSelected = null;
        foreach (var pclDir in pclExeDirs)
        {
            foreach (var folder in FoldersNear(pclDir))
                Add(folder, "当前文件夹", GameFolderSource.Pcl);
            var setup = await PclIniFile.LoadAsync(Path.Combine(pclDir, "PCL", "Setup.ini"), cancellationToken).ConfigureAwait(false);
            if (setup.GetString("LaunchFolderSelect") is { Length: > 0 } selected)
                pclSelected ??= Normalize(selected.Replace("$", AppendSeparator(pclDir), StringComparison.Ordinal));
        }
        foreach (var (name, path) in ReadPclCustomFolders())
        {
            if (Directory.Exists(Path.Combine(path, "versions")))
                Add(path, name, GameFolderSource.Pcl);
        }

        // 3. 官方启动器
        var official = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        if (Directory.Exists(Path.Combine(official, "versions")))
            Add(official, "官方启动器文件夹", GameFolderSource.Official);

        // 4. 用户添加的文件夹；应用用户的重命名与“移出列表”
        foreach (var entry in settings.Folders)
        {
            if (entry.IsRemoved)
                continue;
            var existing = found.FindIndex(f => SamePath(f.Path, entry.Path));
            if (existing >= 0)
            {
                if (!string.IsNullOrWhiteSpace(entry.Name))
                    found[existing] = found[existing] with { DisplayName = entry.Name };
            }
            else if (Directory.Exists(entry.Path))
            {
                Add(entry.Path, entry.Name ?? Path.GetFileName(Path.GetDirectoryName(Normalize(entry.Path)) ?? entry.Path), GameFolderSource.User);
            }
        }
        found.RemoveAll(f => settings.Folders.Any(e => e.IsRemoved && SamePath(e.Path, f.Path)));

        logger.LogInformation("发现 {Count} 个游戏文件夹：{Folders}", found.Count, string.Join("；", found.Select(f => $"{f.DisplayName}({f.Source}) {f.Path}")));
        return new GameFolderDiscoveryResult(found, pclSelected, pclExeDirs);
    }

    /// <summary>目录本身含 versions，或其子目录含 versions / 名为 .minecraft。</summary>
    private static IEnumerable<string> FoldersNear(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;
        if (Directory.Exists(Path.Combine(directory, "versions")))
            yield return directory;
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            yield break;
        }
        foreach (var child in children)
        {
            if (string.Equals(Path.GetFileName(child), ".minecraft", StringComparison.OrdinalIgnoreCase) || Directory.Exists(Path.Combine(child, "versions")))
                yield return child;
        }
    }

    private IEnumerable<(string Name, string Path)> ReadPclCustomFolders()
    {
        foreach (var key in PclRegistryKeys)
        {
            var value = registry.ReadString(RegistryHive.CurrentUser, key, "LaunchFolders");
            if (string.IsNullOrEmpty(value))
                continue;
            // 格式：名称>路径\|名称>路径\（docs/02 §2）
            foreach (var item in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = item.IndexOf('>');
                if (separator <= 0)
                    continue;
                yield return (item[..separator], item[(separator + 1)..]);
            }
        }
    }

    /// <summary>
    /// 注册表里没有保存 PCL.exe 的路径，按以下线索查找（docs/02 §8）：
    /// 本启动器所在目录及其上级、PCL 自定义文件夹的上级目录、桌面和开始菜单中的快捷方式。
    /// </summary>
    private List<string> FindPclExecutableDirectories(string executableDirectory)
    {
        var candidates = new List<string>();
        void Consider(string? dir)
        {
            for (var depth = 0; dir is not null && depth < 4; depth++, dir = Path.GetDirectoryName(dir))
            {
                if (IsPclDirectory(dir) && !candidates.Any(c => SamePath(c, dir)))
                    candidates.Add(Normalize(dir));
            }
        }

        Consider(executableDirectory);
        foreach (var (_, path) in ReadPclCustomFolders())
            Consider(Path.GetDirectoryName(Normalize(path)));
        foreach (var target in PclShortcutTargets())
            Consider(Path.GetDirectoryName(target));
        return candidates;
    }

    private IEnumerable<string> PclShortcutTargets()
    {
        Environment.SpecialFolder[] roots =
        [
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.CommonDesktopDirectory,
            Environment.SpecialFolder.Programs,
            Environment.SpecialFolder.CommonPrograms,
        ];
        foreach (var root in roots)
        {
            var dir = Environment.GetFolderPath(root);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                continue;
            IEnumerable<string> links;
            try
            {
                links = Directory.EnumerateFiles(dir, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var link in links)
            {
                var name = Path.GetFileNameWithoutExtension(link);
                if (!name.Contains("PCL", StringComparison.OrdinalIgnoreCase) && !name.Contains("Plain Craft", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (shell.ResolveShortcut(link) is { } target && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    yield return target;
            }
        }
    }

    private static bool IsPclDirectory(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, "PCL", "Setup.ini")) &&
                   Directory.EnumerateFiles(dir, "*.exe").Any(f => Path.GetFileName(f).StartsWith("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool SamePath(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string AppendSeparator(string dir) => Path.EndsInDirectorySeparator(dir) ? dir : dir + Path.DirectorySeparatorChar;
}

/// <summary>folders.json 的读写。</summary>
public sealed class FolderSettingsStore(AppPaths paths)
{
    public async Task<FolderSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<FolderSettings>(paths.FoldersFile, cancellationToken).ConfigureAwait(false) ?? new FolderSettings();

    public Task SaveAsync(FolderSettings settings, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(paths.FoldersFile, settings, cancellationToken);
}

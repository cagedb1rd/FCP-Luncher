using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FCP.Core;
using FCP.Core.Accounts;
using FCP.Core.Folders;
using FCP.Core.Instances;
using FCP.Core.Java;
using FCP.Core.Migration;
using FCP.Core.Migration.Pcl;
using FCP.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FCP.App.ViewModels;

/// <summary>界面上的一个游戏文件夹。</summary>
public sealed record FolderItem(GameFolder Folder)
{
    public string Name => Folder.DisplayName;
    public string Path => Folder.Path;
    public string Source => Folder.Source switch
    {
        GameFolderSource.Pcl => "PCL",
        GameFolderSource.Official => "官方",
        GameFolderSource.Portable => "当前",
        _ => "手动",
    };
}

/// <summary>
/// 启动器的共享状态：游戏文件夹、版本、当前选择、账号、Java。各页面共用同一个实例。
/// </summary>
public sealed partial class LauncherState(
    GameFolderDiscovery discovery,
    FolderSettingsStore folderStore,
    InstanceScanner scanner,
    InstanceCatalog catalog,
    AccountService accounts,
    JavaManager javaManager,
    PclMigrationService migration,
    AppPaths paths,
    ILogger<LauncherState> logger) : ObservableObject
{
    private FolderSettings _folderSettings = new();
    private GameFolderDiscoveryResult? _lastDiscovery;
    private string? _pclSelectedFolder;

    public ObservableCollection<FolderItem> Folders { get; } = [];
    public ObservableCollection<InstanceItem> Instances { get; } = [];
    public ObservableCollection<AccountRecord> Accounts { get; } = [];

    public AppPaths Paths => paths;

    /// <summary>是否检测到 PCL（用于账号对话框默认使用 PCL 兼容 UUID 等）。</summary>
    public bool HasPcl { get; private set; }

    [ObservableProperty]
    private FolderItem? _selectedFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstance))]
    private InstanceItem? _selectedInstance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayerName), nameof(AccountText), nameof(HasAccount))]
    private AccountRecord? _account;

    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>从 PCL 导入的结果（没有 PCL 时为 null）。</summary>
    [ObservableProperty]
    private PclImportRecord? _pclImport;

    [ObservableProperty]
    private IReadOnlyList<JavaRuntime> _javas = [];

    public bool HasInstance => SelectedInstance is not null;
    public bool HasAccount => Account is not null;
    public string PlayerName => Account?.PlayerName ?? "未登录";
    public string AccountText => Account?.KindText ?? "点击添加账号";

    /// <summary>新发现的 Java（用于提示）。</summary>
    public event EventHandler<IReadOnlyList<JavaRuntime>>? JavaDiscovered;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            _folderSettings = await folderStore.LoadAsync(cancellationToken);
            await RefreshFoldersAsync(cancellationToken);
            await RefreshAccountsAsync(cancellationToken);
            javaManager.Refreshed += (_, added) =>
            {
                Javas = javaManager.Current;
                if (added.Count > 0)
                    JavaDiscovered?.Invoke(this, added);
            };
            // Java 在后台扫描，不阻塞界面
            _ = Task.Run(() => javaManager.RefreshAsync(CancellationToken.None), CancellationToken.None);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshFoldersAsync(CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(() => discovery.DiscoverAsync(paths.ExecutableDirectory, _folderSettings, cancellationToken), cancellationToken);
        _lastDiscovery = result;
        // 第一次打开时从 PCL 导入设置和账号（只导入一次）；必须在扫描版本之前，版本隔离的结果依赖导入的设置
        try
        {
            PclImport = await Task.Run(() => migration.RunOnceAsync(result, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UserFacingException)
        {
            logger.LogWarning(ex, "从 PCL 导入设置失败");
        }
        _pclSelectedFolder = result.PclSelectedFolder;
        HasPcl = result.PclExecutableDirectories.Count > 0 || result.Folders.Any(f => f.Source == GameFolderSource.Pcl);
        Folders.Clear();
        foreach (var folder in result.Folders)
            Folders.Add(new FolderItem(folder));

        // 选择顺序：上次的选择 → PCL 上次的选择 → 第一个
        var target = Folders.FirstOrDefault(f => _folderSettings.SelectedFolder is { } saved && GameFolderDiscovery.SamePath(saved, f.Path))
            ?? Folders.FirstOrDefault(f => _pclSelectedFolder is { } pcl && GameFolderDiscovery.SamePath(pcl, f.Path))
            ?? Folders.FirstOrDefault();
        await SelectFolderAsync(target, save: false, cancellationToken);
    }

    public async Task SelectFolderAsync(FolderItem? folder, bool save = true, CancellationToken cancellationToken = default)
    {
        SelectedFolder = folder;
        if (save && folder is not null)
        {
            _folderSettings.SelectedFolder = folder.Path;
            await folderStore.SaveAsync(_folderSettings, cancellationToken);
        }
        await ReloadInstancesAsync(cancellationToken);
    }

    public async Task ReloadInstancesAsync(CancellationToken cancellationToken = default)
    {
        var previous = SelectedInstance?.Name;
        Instances.Clear();
        if (SelectedFolder is null)
        {
            SelectedInstance = null;
            return;
        }
        var folder = SelectedFolder.Path;
        var entries = await Task.Run(async () => await catalog.BuildEntriesAsync(await scanner.ScanAsync(folder, cancellationToken), cancellationToken), cancellationToken);
        foreach (var entry in entries)
            Instances.Add(new InstanceItem(entry));

        // 选择顺序：刚才的选择 → 该文件夹上次的选择 → PCL.ini 中的选择 → 最近游玩的正常版本
        _folderSettings.SelectedInstances.TryGetValue(folder, out var saved);
        var pclChoice = (await PclIniFile.LoadAsync(System.IO.Path.Combine(folder, "PCL.ini"), cancellationToken)).GetString("Version");
        SelectedInstance = Find(previous) ?? Find(saved) ?? Find(pclChoice)
            ?? Instances.Where(i => i.IsHealthy && i.Entry.Group != InstanceGroup.Hidden).OrderByDescending(i => i.Entry.LastPlayed ?? DateTimeOffset.MinValue).FirstOrDefault();

        InstanceItem? Find(string? name) => name is null ? null : Instances.FirstOrDefault(i => i.Name == name && i.IsHealthy);
    }

    public async Task SelectInstanceAsync(InstanceItem item, CancellationToken cancellationToken = default)
    {
        SelectedInstance = item;
        if (SelectedFolder is not null)
        {
            _folderSettings.SelectedInstances[SelectedFolder.Path] = item.Name;
            await folderStore.SaveAsync(_folderSettings, cancellationToken);
        }
    }

    /// <summary>
    /// 按版本目录选中版本（桌面快捷方式 --launch 使用）：<c>…\.minecraft\versions\&lt;名称&gt;</c>。
    /// 所在的游戏文件夹不在列表中时会自动添加。
    /// </summary>
    public async Task<bool> SelectByVersionDirectoryAsync(string versionDirectory, CancellationToken cancellationToken = default)
    {
        var dir = GameFolderDiscovery.Normalize(versionDirectory);
        var name = System.IO.Path.GetFileName(dir);
        var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(dir)) ?? "";
        if (root.Length == 0 || !Directory.Exists(dir))
            return false;

        var folder = Folders.FirstOrDefault(f => GameFolderDiscovery.SamePath(f.Path, root));
        if (folder is null)
        {
            await AddFolderAsync(root, cancellationToken);
            folder = Folders.FirstOrDefault(f => GameFolderDiscovery.SamePath(f.Path, root));
        }
        if (folder is null)
            return false;
        if (!ReferenceEquals(folder, SelectedFolder))
            await SelectFolderAsync(folder, save: false, cancellationToken);

        var item = Instances.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return false;
        SelectedInstance = item;
        return true;
    }

    public async Task ToggleFavoriteAsync(InstanceItem item, CancellationToken cancellationToken = default)
    {
        var meta = item.Entry.Meta;
        meta.IsFavorite = !item.IsFavorite;
        await catalog.SaveMetaAsync(item.Instance, meta, cancellationToken);
        item.Entry = item.Entry with { IsFavorite = meta.IsFavorite.Value, Group = InstanceCatalog.Classify(item.Instance, meta.IsFavorite.Value, item.Entry.Group == InstanceGroup.Hidden) };
        OnPropertyChanged(nameof(Instances));
    }

    /// <summary>游戏退出后刷新该版本的游玩记录。</summary>
    public async Task RefreshInstanceAsync(InstanceItem item, CancellationToken cancellationToken = default)
    {
        var meta = await catalog.LoadMetaAsync(item.Instance, cancellationToken);
        item.Entry = item.Entry with { Meta = meta, LastPlayed = meta.LastPlayed ?? item.Entry.LastPlayed };
    }

    public async Task AddFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalized = GameFolderDiscovery.Normalize(path);
        // 选中的是 .minecraft 的上一级，并且其中有 .minecraft
        if (!Directory.Exists(System.IO.Path.Combine(normalized, "versions")) && Directory.Exists(System.IO.Path.Combine(normalized, ".minecraft", "versions")))
            normalized = System.IO.Path.Combine(normalized, ".minecraft");
        _folderSettings.Folders.RemoveAll(f => GameFolderDiscovery.SamePath(f.Path, normalized));
        _folderSettings.Folders.Add(new FolderEntry { Path = normalized });
        _folderSettings.SelectedFolder = normalized;
        await folderStore.SaveAsync(_folderSettings, cancellationToken);
        logger.LogInformation("添加游戏文件夹：{Path}", normalized);
        await RefreshFoldersAsync(cancellationToken);
    }

    /// <summary>设置 → 其他 → 重新从 PCL 导入：覆盖对应的全局设置，然后刷新版本和账号。</summary>
    public async Task<PclImportRecord?> ReimportFromPclAsync(CancellationToken cancellationToken = default)
    {
        var result = _lastDiscovery ?? await Task.Run(() => discovery.DiscoverAsync(paths.ExecutableDirectory, _folderSettings, cancellationToken), cancellationToken);
        PclImport = await Task.Run(() => migration.ImportAsync(result, cancellationToken), cancellationToken);
        await ReloadInstancesAsync(cancellationToken);
        await RefreshAccountsAsync(cancellationToken);
        return PclImport;
    }

    public async Task RefreshAccountsAsync(CancellationToken cancellationToken = default)
    {
        Accounts.Clear();
        foreach (var account in await accounts.ListAsync(cancellationToken))
            Accounts.Add(account);
        Account = await accounts.GetSelectedAsync(cancellationToken);
    }

    public async Task<AccountRecord> AddOfflineAccountAsync(string name, UuidScheme scheme, CancellationToken cancellationToken = default)
    {
        var account = await accounts.AddOfflineAsync(name, scheme, cancellationToken);
        await RefreshAccountsAsync(cancellationToken);
        return account;
    }

    public async Task SelectAccountAsync(AccountRecord account, CancellationToken cancellationToken = default)
    {
        await accounts.SelectAsync(account.Id, cancellationToken);
        Account = account;
    }

    public async Task RemoveAccountAsync(AccountRecord account, CancellationToken cancellationToken = default)
    {
        await accounts.RemoveAsync(account.Id, cancellationToken);
        await RefreshAccountsAsync(cancellationToken);
    }
}

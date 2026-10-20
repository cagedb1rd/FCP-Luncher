using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCP.Core.Instances;
using FCP.Core.Launch;
using FCP.Core.Migration;
using FCP.Core.Platform;

namespace FCP.App.ViewModels;

/// <summary>版本设置的分页，顺序与 PCL 相同（docs/05 §4.3）。</summary>
public enum InstanceSection
{
    Overview,
    Settings,
    Mods,
    ResourcePacks,
    Shaders,
    Saves,
}

/// <summary>概览和设置页中的一行“名称：值”。</summary>
public sealed record InfoRow(string Label, string Value, string? Hint = null);

/// <summary>Mod 列表中的一项。</summary>
public sealed partial class ModItem : ObservableObject
{
    private readonly Func<ModItem, bool, Task> _onToggle;
    private bool _suppressToggle;

    public ModItem(ModFile file, Func<ModItem, bool, Task> onToggle)
    {
        _file = file;
        _isEnabled = file.IsEnabled;
        _onToggle = onToggle;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private ModFile _file;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Version), nameof(Loader), nameof(Description))]
    private ModMetadata? _meta;

    [ObservableProperty]
    private bool _isEnabled;

    public string Name => Meta?.Name ?? Path.GetFileNameWithoutExtension(File.BaseName);
    public string? Version => Meta?.Version;
    public string? Loader => Meta?.Loader;
    public string? Description => Meta?.Description;
    public string FileName => File.FileName;

    /// <summary>由代码改变状态（例如操作失败后恢复），不触发重命名。</summary>
    public void SetEnabledSilently(bool value)
    {
        _suppressToggle = true;
        IsEnabled = value;
        _suppressToggle = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_suppressToggle)
            _ = _onToggle(this, value);
    }

    public bool Matches(string query) =>
        query.Length == 0 ||
        Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (Meta?.Id?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>存档 / 资源包 / 光影列表中的一项。</summary>
public sealed record ContentItem(string Name, string Path, string Detail, string? IconPath = null, bool IsFolder = true);

/// <summary>
/// 版本设置（子页面）：概览、设置、Mod、资源包、光影、存档。
/// 显示的都是从本地游戏目录读取的内容（产品原则 4：PCL 中的 Mod 和存档直接可见）。
/// </summary>
public sealed partial class InstanceSettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly LaunchService _launchService;
    private readonly IShell _platformShell;
    private List<ModItem> _allMods = [];
    private bool _modsLoaded;
    private bool _contentLoaded;

    public InstanceSettingsViewModel(ShellViewModel shell, LauncherState state, InstanceItem item, LaunchService launchService, IShell platformShell)
    {
        _shell = shell;
        State = state;
        Item = item;
        _launchService = launchService;
        _platformShell = platformShell;
        _ = LoadOverviewAsync();
    }

    public LauncherState State { get; }
    public InstanceItem Item { get; }
    private InstanceEntry Entry => Item.Entry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsSettings), nameof(IsMods), nameof(IsResourcePacks), nameof(IsShaders), nameof(IsSaves))]
    private InstanceSection _section = InstanceSection.Overview;

    public bool IsOverview => Section == InstanceSection.Overview;
    public bool IsSettings => Section == InstanceSection.Settings;
    public bool IsMods => Section == InstanceSection.Mods;
    public bool IsResourcePacks => Section == InstanceSection.ResourcePacks;
    public bool IsShaders => Section == InstanceSection.Shaders;
    public bool IsSaves => Section == InstanceSection.Saves;

    partial void OnSectionChanged(InstanceSection value)
    {
        if (value == InstanceSection.Mods && !_modsLoaded)
            _ = LoadModsAsync();
        if (value is InstanceSection.Saves or InstanceSection.ResourcePacks or InstanceSection.Shaders && !_contentLoaded)
            LoadContent();
    }

    // ---------------- 概览 ----------------

    public ObservableCollection<InfoRow> Overview { get; } = [];
    public ObservableCollection<InfoRow> EffectiveSettings { get; } = [];

    private async Task LoadOverviewAsync()
    {
        var entry = Entry;
        var instance = entry.Instance;
        Overview.Clear();
        Overview.Add(new InfoRow("Minecraft 版本", instance.MinecraftVersion ?? "未知"));
        Overview.Add(new InfoRow("加载器", instance.Loaders.Count == 0 ? "无（原版）" : string.Join("、", instance.Loaders.Select(l => l.DisplayName))));
        Overview.Add(new InfoRow("游戏目录", entry.GameDirectory, entry.IsIsolated ? "已开启版本隔离：存档、Mod 和设置只属于这个版本" : "未开启版本隔离：与同一文件夹中的其他版本共用"));
        Overview.Add(new InfoRow("上次游玩", Item.LastPlayedText));
        var meta = entry.Meta;
        if (meta.LaunchCount > 0 || meta.TotalPlaySeconds > 0)
            Overview.Add(new InfoRow("在 FCP 中", $"启动 {meta.LaunchCount} 次，{Item.PlayTimeText ?? "游玩不到 1 分钟"}"));

        // 生效的设置及来源（与启动时的规则相同）
        EffectiveSettings.Clear();
        EffectiveSettings.Add(new InfoRow("版本隔离", entry.IsIsolated ? "开启" : "关闭", IsolationSource(entry)));
        try
        {
            var (_, memory) = await _launchService.PreviewMemoryAsync(entry);
            EffectiveSettings.Add(new InfoRow("游戏内存", memory));
        }
        catch (IOException)
        {
        }
        EffectiveSettings.Add(new InfoRow("Java", _launchService.PreviewJava(entry)));
        var pcl = entry.Pcl;
        if (pcl?.ExtraJvmArguments is { } jvm)
            EffectiveSettings.Add(new InfoRow("JVM 参数", jvm, "沿用 PCL 中的版本设置"));
        if (pcl?.AutoJoinServer is { } server)
            EffectiveSettings.Add(new InfoRow("自动进入服务器", server, "沿用 PCL 中的版本设置"));
        if (pcl is { LoginRequirement: > 0 })
        {
            EffectiveSettings.Add(new InfoRow("登录要求", pcl.LoginRequirement switch
            {
                1 => "仅正版",
                2 => "仅离线",
                3 => "统一通行证（FCP 不支持）",
                4 => $"{AccountClues.ServerDisplayName(pcl.AuthServerName, pcl.AuthServer ?? "")} 登录",
                _ => "正版或离线",
            }, "沿用 PCL 中的版本设置"));
        }
        var clue = AccountClues.Collect(entry).FirstOrDefault();
        if (clue is not null)
            Overview.Add(new InfoRow("存档的主人", clue.PlayerName, clue.Kind switch
            {
                AccountClueKind.PclOffline => "PCL 离线账号（FCP 中使用相同的 UUID，进度不变）",
                AccountClueKind.StandardOffline => "离线账号",
                AccountClueKind.SkinSite => $"{clue.ServerName} 角色",
                _ => "很可能是正版账号",
            }));
    }

    private static string IsolationSource(InstanceEntry entry) =>
        entry.Meta.Settings.Isolated is not null ? "FCP 中的版本设置"
        : entry.Pcl?.Isolated is not null ? "沿用 PCL 中的版本设置"
        : "按全局设置和版本内容自动判断（与 PCL 的规则相同）";

    public bool IsFavorite => Item.IsFavorite;

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        await State.ToggleFavoriteAsync(Item);
        OnPropertyChanged(nameof(IsFavorite));
    }

    /// <summary>打开文件夹：version / game / mods / saves / resourcepacks / shaderpacks / screenshots / logs。</summary>
    [RelayCommand]
    private void OpenFolder(string kind)
    {
        var path = kind switch
        {
            "version" => Entry.Instance.VersionDirectory,
            "game" => Entry.GameDirectory,
            _ => Path.Combine(Entry.GameDirectory, kind),
        };
        if (!Directory.Exists(path))
        {
            _shell.ShowHint("这个文件夹还不存在（游戏还没有生成它）");
            return;
        }
        _platformShell.OpenFolder(path);
    }

    // ---------------- Mod ----------------

    public ObservableCollection<ModItem> Mods { get; } = [];

    [ObservableProperty]
    private string _modSearch = "";

    [ObservableProperty]
    private string _modSummary = "正在读取 Mod…";

    [ObservableProperty]
    private bool _isLoadingMods;

    partial void OnModSearchChanged(string value) => FilterMods();

    private async Task LoadModsAsync()
    {
        _modsLoaded = true;
        IsLoadingMods = true;
        try
        {
            var files = await Task.Run(() => InstanceContent.ListMods(Entry.GameDirectory));
            _allMods = files.Select(f => new ModItem(f, ToggleModAsync)).ToList();
            FilterMods();
            // 在后台读取每个 Mod 的名称和版本，读到一个更新一个
            var items = _allMods.ToList();
            await Task.Run(() => Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = 4 }, mod =>
            {
                var meta = InstanceContent.ReadMetadata(mod.File.Path);
                if (meta is not null)
                    Ui.Post(() => mod.Meta = meta);
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shell.ShowError(ex);
        }
        finally
        {
            IsLoadingMods = false;
            UpdateModSummary();
        }
    }

    private void FilterMods()
    {
        var query = ModSearch.Trim();
        Mods.Clear();
        foreach (var mod in _allMods.Where(m => m.Matches(query)))
            Mods.Add(mod);
        UpdateModSummary();
    }

    private void UpdateModSummary()
    {
        var enabled = _allMods.Count(m => m.IsEnabled);
        ModSummary = _allMods.Count == 0
            ? "这个版本还没有安装 Mod"
            : $"共 {_allMods.Count} 个，已启用 {enabled} 个" + (enabled < _allMods.Count ? $"，已禁用 {_allMods.Count - enabled} 个" : "");
    }

    private async Task ToggleModAsync(ModItem mod, bool enabled)
    {
        try
        {
            mod.File = await Task.Run(() => InstanceContent.SetEnabled(mod.File, enabled));
            Item.InvalidateModCount();
            UpdateModSummary();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FCP.Core.UserFacingException)
        {
            mod.SetEnabledSilently(!enabled);
            _shell.ShowError(ex is IOException io
                ? new FCP.Core.UserFacingException(enabled ? "无法启用这个 Mod" : "无法禁用这个 Mod", "文件可能正在被游戏使用。请先关闭游戏再试。", io)
                : ex);
        }
    }

    // ---------------- 资源包 / 光影 / 存档 ----------------

    public ObservableCollection<ContentItem> ResourcePacks { get; } = [];
    public ObservableCollection<ContentItem> Shaders { get; } = [];
    public ObservableCollection<ContentItem> Saves { get; } = [];

    private void LoadContent()
    {
        _contentLoaded = true;
        var dir = Entry.GameDirectory;
        foreach (var save in InstanceContent.ListSaves(dir))
            Saves.Add(new ContentItem(save.Name, save.Path, $"上次游玩 {InstanceItem.RelativeTime(save.LastPlayed)}", save.IconPath));
        foreach (var pack in InstanceContent.ListPacks(dir, "resourcepacks"))
            ResourcePacks.Add(new ContentItem(pack.Name, pack.Path, pack.IsFolder ? "文件夹" : "压缩包", IsFolder: pack.IsFolder));
        foreach (var pack in InstanceContent.ListPacks(dir, "shaderpacks"))
            Shaders.Add(new ContentItem(pack.Name, pack.Path, pack.IsFolder ? "文件夹" : "压缩包", IsFolder: pack.IsFolder));
        OnPropertyChanged(nameof(SavesSummary));
        OnPropertyChanged(nameof(ResourcePacksSummary));
        OnPropertyChanged(nameof(ShadersSummary));
    }

    public string SavesSummary => Saves.Count == 0 ? "这个版本还没有存档" : $"共 {Saves.Count} 个存档";
    public string ResourcePacksSummary => ResourcePacks.Count == 0 ? "还没有资源包" : $"共 {ResourcePacks.Count} 个资源包";
    public string ShadersSummary => Shaders.Count == 0 ? "还没有光影包" : $"共 {Shaders.Count} 个光影包";

    /// <summary>打开存档或资源包所在位置。</summary>
    [RelayCommand]
    private void OpenItem(ContentItem item) => _platformShell.OpenFolder(item.IsFolder ? item.Path : Path.GetDirectoryName(item.Path)!);
}

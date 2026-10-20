using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCP.Core.Instances;
using FCP.Core.Platform;

namespace FCP.App.ViewModels;

/// <summary>版本列表中的一个分组。</summary>
public sealed record InstanceGroupView(InstanceGroup Group, string Title, string Icon, IReadOnlyList<InstanceItem> Items)
{
    public string CountText => $"{Items.Count} 个";
}

/// <summary>
/// 版本选择（子页面）：左边是游戏文件夹列表，右边是分组的版本卡片；点击版本即选中并返回启动页（与 PCL 一致）。
/// </summary>
public sealed partial class InstanceSelectViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly IShell _platformShell;

    public InstanceSelectViewModel(ShellViewModel shell, LauncherState state, IShell platformShell)
    {
        _shell = shell;
        State = state;
        _platformShell = platformShell;
        state.Instances.CollectionChanged += (_, _) => Regroup();
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherState.Instances))
                Regroup();
            else if (e.PropertyName == nameof(LauncherState.IsLoading))
                OnPropertyChanged(nameof(IsEmpty));
        };
    }

    public LauncherState State { get; }

    public ObservableCollection<InstanceGroupView> Groups { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _showHidden;

    public bool IsEmpty => !State.IsLoading && State.Instances.Count == 0;

    partial void OnSearchTextChanged(string value) => Regroup();

    partial void OnShowHiddenChanged(bool value) => Regroup();

    private void Regroup()
    {
        Groups.Clear();
        var query = SearchText.Trim();
        var items = State.Instances.Where(i =>
            query.Length == 0 ||
            i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            i.Info.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var group in items.GroupBy(i => i.Entry.Group).OrderBy(g => g.Key))
        {
            if (group.Key == InstanceGroup.Hidden && !ShowHidden)
                continue;
            var (title, icon) = group.Key switch
            {
                InstanceGroup.Favorite => ("收藏", "Icon.StarFilled"),
                InstanceGroup.Modded => ("可安装 Mod", "Icon.Puzzle"),
                InstanceGroup.Regular => ("常规版本", "Icon.Box"),
                InstanceGroup.Rare => ("不常用版本", "Icon.Clock"),
                InstanceGroup.AprilFools => ("愚人节版本", "Icon.Sparkle"),
                InstanceGroup.Broken => ("有问题的版本", "Icon.Info"),
                _ => ("已隐藏", "Icon.Grid"),
            };
            Groups.Add(new InstanceGroupView(group.Key, title, icon, group.ToList()));
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private Task SelectFolderAsync(FolderItem folder) => State.SelectFolderAsync(folder);

    [RelayCommand]
    private async Task SelectInstanceAsync(InstanceItem item)
    {
        if (!item.IsHealthy)
        {
            _shell.ShowHint($"{item.Name}：{item.Info}", HintKind.Error);
            return;
        }
        await State.SelectInstanceAsync(item);
        _shell.BackCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LaunchInstanceAsync(InstanceItem item)
    {
        await State.SelectInstanceAsync(item);
        _shell.BackCommand.Execute(null);
        await _shell.Launch.LaunchCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(InstanceItem item)
    {
        await State.ToggleFavoriteAsync(item);
        Regroup();
    }

    [RelayCommand]
    private void OpenSettings(InstanceItem item) => _shell.OpenInstanceSettings(item);

    [RelayCommand]
    private void OpenFolder(InstanceItem item) => _platformShell.OpenFolder(item.Entry.GameDirectory);

    /// <summary>PCL 没有的功能：为版本创建桌面快捷方式，双击直接启动该版本。</summary>
    [RelayCommand]
    private void CreateShortcut(InstanceItem item)
    {
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定启动器的位置");
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var safeName = string.Concat(item.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var link = Path.Combine(desktop, $"{safeName}.lnk");
            _platformShell.CreateShortcut(link, exe, $"--launch \"{item.Instance.VersionDirectory}\"", Path.GetDirectoryName(exe), $"启动 {item.Name}");
            _shell.ShowHint($"已在桌面创建快捷方式：{safeName}", HintKind.Success);
        }
        catch (Exception ex)
        {
            _shell.ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择游戏文件夹（.minecraft，或包含 .minecraft 的文件夹）" };
        if (dialog.ShowDialog() != true)
            return;
        var path = dialog.FolderName;
        if (!Directory.Exists(Path.Combine(path, "versions")) && !Directory.Exists(Path.Combine(path, ".minecraft", "versions")))
        {
            _shell.ShowHint("这个文件夹里没有找到游戏版本（需要有 versions 文件夹）", HintKind.Error);
            return;
        }
        await State.AddFolderAsync(path);
        _shell.ShowHint("已添加游戏文件夹", HintKind.Success);
    }

    [RelayCommand]
    private Task RefreshAsync() => State.RefreshFoldersAsync();
}

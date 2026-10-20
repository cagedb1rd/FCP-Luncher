using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCP.UI.Controls;

namespace FCP.App.ViewModels;

public sealed record FolderItem(string Name, string Path, string Source, int InstanceCount);

public sealed record InstanceGroup(string Title, string Icon, IReadOnlyList<InstanceSummary> Items)
{
    public string CountText => $"{Items.Count} 个";
}

/// <summary>
/// 版本选择（子页面）：左边是游戏文件夹列表，右边是分组的版本卡片。
/// 原型阶段使用示例数据。
/// </summary>
public sealed partial class InstanceSelectViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly LaunchPageViewModel _launch;
    private readonly IReadOnlyList<InstanceGroup> _allGroups;

    public InstanceSelectViewModel(ShellViewModel shell, LaunchPageViewModel launch)
    {
        _shell = shell;
        _launch = launch;
        Folders =
        [
            new FolderItem("当前文件夹", @"E:\MC\PCL\.minecraft", "PCL", 1),
            new FolderItem("xiangcao", @"E:\MC\PCL\xiangcao\.minecraft", "PCL", 1),
        ];
        _selectedFolder = Folders[0];
        _allGroups =
        [
            new InstanceGroup("收藏", "Icon.StarFilled", [launch.Recent[0]]),
            // 与 PCL 一致：收藏的版本只出现在“收藏”分组中
            new InstanceGroup("可安装 Mod", "Icon.Puzzle",
            [
                launch.Recent[1],
                new InstanceSummary("Fabric 1.21.4 生存", "1.21.4", "Fabric 0.16.10", null, BlockKind.Fabric, "上周", 46),
                new InstanceSummary("NeoForge 1.21.1 科技", "1.21.1", "NeoForge 21.1.90", null, BlockKind.NeoForge, "从未游玩", 212),
            ]),
            new InstanceGroup("常规版本", "Icon.Box",
            [
                new InstanceSummary("1.21.4", "1.21.4", null, null, BlockKind.Vanilla, "昨天"),
                new InstanceSummary("1.8.9 OptiFine", "1.8.9", "OptiFine HD U M5", null, BlockKind.OptiFine, "3 个月前"),
            ]),
            new InstanceGroup("不常用版本", "Icon.Clock",
            [
                new InstanceSummary("26.4-snapshot-5", "26.4-snapshot-5", null, null, BlockKind.Snapshot, "从未游玩"),
                new InstanceSummary("b1.7.3", "Beta 1.7.3", null, null, BlockKind.Old, "去年"),
            ]),
        ];
        Groups = new ObservableCollection<InstanceGroup>(_allGroups);
    }

    public ObservableCollection<FolderItem> Folders { get; }

    public ObservableCollection<InstanceGroup> Groups { get; }

    [ObservableProperty]
    private FolderItem _selectedFolder;

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        Groups.Clear();
        foreach (var group in _allGroups)
        {
            var items = group.Items.Where(i => i.Name.Contains(value, StringComparison.OrdinalIgnoreCase) || i.Info.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count > 0)
                Groups.Add(group with { Items = items });
        }
    }

    [RelayCommand]
    private void SelectFolder(FolderItem folder) => SelectedFolder = folder;

    [RelayCommand]
    private void SelectInstance(InstanceSummary instance)
    {
        _launch.SelectInstance(instance);
        _shell.BackCommand.Execute(null);
    }

    [RelayCommand]
    private void AddFolder() => _shell.ShowHint("添加文件夹将在 P1 实现（原型）");
}

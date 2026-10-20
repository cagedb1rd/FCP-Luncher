using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FCP.App.ViewModels;

public sealed record FolderItem(string Name, string Path, string Source);

public sealed record InstanceGroup(string Title, IReadOnlyList<InstanceSummary> Items);

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
            new FolderItem("当前文件夹", @"E:\MC\PCL\.minecraft", "来自 PCL"),
            new FolderItem("xiangcao", @"E:\MC\PCL\xiangcao\.minecraft", "来自 PCL"),
        ];
        _selectedFolder = Folders[0];
        _allGroups =
        [
            new InstanceGroup("可安装 Mod",
            [
                new InstanceSummary("FarmingTales_ForgeⅡ v1.4.3", "1.20.1 · Forge 47.4.10 · LittleSkin", "3 天前"),
            ]),
            new InstanceGroup("常规版本",
            [
                new InstanceSummary("1.21.4", "正式版 1.21.4", "从未游玩"),
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

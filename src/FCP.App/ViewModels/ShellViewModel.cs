using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FCP.App.ViewModels;

/// <summary>顶部主标签。顺序与 PCL 一致（docs/05 §2）。</summary>
public enum MainTab
{
    Launch,
    Download,
    Link,
    Settings,
    More,
}

public enum HintKind
{
    Info,
    Success,
    Error,
}

public sealed record HintItem(string Text, HintKind Kind);

/// <summary>
/// 外壳：主标签 + 子页面栈。进入子页面（版本选择、版本设置等）时，
/// 标题栏的标签组换成“返回 + 标题”，返回时恢复，与 PCL 的导航方式一致。
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Stack<(object Page, string Title, bool IsSub)> _stack = new();
    private readonly Dictionary<MainTab, object> _tabPages;

    public ShellViewModel()
    {
        Launch = new LaunchPageViewModel(this);
        _tabPages = new Dictionary<MainTab, object>
        {
            [MainTab.Launch] = Launch,
            [MainTab.Download] = new PlaceholderPageViewModel("下载", "安装新版本和下载资源将在 v0.2 推出。"),
            [MainTab.Link] = new PlaceholderPageViewModel("联机", "联机功能仍在评估中。"),
            [MainTab.Settings] = new SettingsPageViewModel(this),
            [MainTab.More] = new PlaceholderPageViewModel("更多", "帮助、关于与鸣谢、工具箱和反馈将在后续阶段实现。"),
        };
        _currentPage = Launch;
    }

    public LaunchPageViewModel Launch { get; }

    /// <summary>MVP 阶段隐藏“下载”和“联机”标签（D-010）。</summary>
    public bool IsDownloadTabVisible => false;

    public bool IsLinkTabVisible => false;

    [ObservableProperty]
    private MainTab _selectedTab = MainTab.Launch;

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private bool _isSubPage;

    [ObservableProperty]
    private string _subPageTitle = "";

    public ObservableCollection<HintItem> Hints { get; } = [];

    partial void OnSelectedTabChanged(MainTab value)
    {
        _stack.Clear();
        IsSubPage = false;
        CurrentPage = _tabPages[value];
    }

    public void OpenSubPage(object page, string title)
    {
        _stack.Push((CurrentPage, SubPageTitle, IsSubPage));
        CurrentPage = page;
        SubPageTitle = title;
        IsSubPage = true;
    }

    [RelayCommand]
    private void Back()
    {
        if (!_stack.TryPop(out var previous))
            return;
        CurrentPage = previous.Page;
        SubPageTitle = previous.Title;
        IsSubPage = previous.IsSub;
    }

    /// <summary>在左下角显示一条提示，几秒后自动消失。</summary>
    public void ShowHint(string text, HintKind kind = HintKind.Info)
    {
        var hint = new HintItem(text, kind);
        Hints.Add(hint);
        _ = RemoveHintLaterAsync(hint);
    }

    private async Task RemoveHintLaterAsync(HintItem hint)
    {
        await Task.Delay(TimeSpan.FromSeconds(3.5));
        Hints.Remove(hint);
    }
}

/// <summary>尚未实现的页面。</summary>
public sealed record PlaceholderPageViewModel(string Title, string Description);

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCP.Core;
using FCP.Core.Java;
using FCP.Core.Launch;
using FCP.Core.Platform;
using FCP.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
/// 外壳：主标签 + 子页面栈 + 提示条 + 对话框。进入子页面（版本选择、版本设置等）时，
/// 标题栏的标签组换成“返回 + 标题”，返回时恢复，与 PCL 的导航方式一致。
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Stack<(object Page, string Title, bool IsSub)> _stack = new();
    private readonly Dictionary<MainTab, object> _tabPages;

    public ShellViewModel(IServiceProvider services)
    {
        Services = services;
        State = ActivatorUtilities.CreateInstance<LauncherState>(services);
        Launch = new LaunchPageViewModel(this, State, services.GetRequiredService<LaunchService>(), services.GetRequiredService<SettingsStore>(),
            services.GetRequiredService<IShell>(), services.GetRequiredService<ILogger<LaunchPageViewModel>>());
        InstanceSelect = new InstanceSelectViewModel(this, State, services.GetRequiredService<IShell>());
        Settings = new SettingsPageViewModel(this, State, services.GetRequiredService<SettingsStore>(), services.GetRequiredService<JavaManager>(), services.GetRequiredService<ISystemInfo>(), services.GetRequiredService<IShell>());
        _tabPages = new Dictionary<MainTab, object>
        {
            [MainTab.Launch] = Launch,
            [MainTab.Download] = new PlaceholderPageViewModel("下载", "Mod、资源包、光影等社区资源的下载将在后续版本推出（FCP 不提供游戏本体下载）。", FCP.UI.Controls.BlockKind.Fabric),
            [MainTab.Link] = new PlaceholderPageViewModel("联机", "联机功能仍在评估中。", FCP.UI.Controls.BlockKind.Quilt),
            [MainTab.Settings] = Settings,
            [MainTab.More] = new PlaceholderPageViewModel("更多", "帮助、关于与鸣谢、工具箱和反馈将在后续阶段实现。", FCP.UI.Controls.BlockKind.AprilFools),
        };
        _currentPage = Launch;
        // PCL 没有的功能：新安装的 Java 会被自动发现并提示
        State.JavaDiscovered += (_, added) => Ui.Post(() => ShowHint($"发现了新安装的 {string.Join("、", added.Select(j => j.DisplayName))}", HintKind.Success));
    }

    public IServiceProvider Services { get; }
    public LauncherState State { get; }
    public LaunchPageViewModel Launch { get; }
    public InstanceSelectViewModel InstanceSelect { get; }
    public SettingsPageViewModel Settings { get; }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private DialogViewModel? _dialog;

    public bool HasDialog => Dialog is not null;

    public ObservableCollection<HintItem> Hints { get; } = [];

    /// <summary>窗口显示后调用：加载文件夹、版本、账号，后台扫描 Java。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await State.InitializeAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    partial void OnSelectedTabChanged(MainTab value)
    {
        _stack.Clear();
        IsSubPage = false;
        CurrentPage = _tabPages[value];
        // 设置可能在别处被修改（例如第一次打开时从 PCL 导入），每次进入设置页时重新读取
        if (value == MainTab.Settings)
            _ = Settings.ReloadAsync();
    }

    /// <summary>打开某个版本的版本设置（子页面）。</summary>
    public void OpenInstanceSettings(InstanceItem item, InstanceSection section = InstanceSection.Overview)
    {
        var page = new InstanceSettingsViewModel(this, State, item, Services.GetRequiredService<LaunchService>(), Services.GetRequiredService<IShell>()) { Section = section };
        OpenSubPage(page, $"版本设置 - {item.Name}");
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

    /// <summary>显示对话框并等待关闭；同一时间只显示一个。</summary>
    public async Task<bool> ShowDialogAsync(DialogViewModel dialog)
    {
        dialog.CloseRequested += (_, _) =>
        {
            if (ReferenceEquals(Dialog, dialog))
                Dialog = null;
        };
        Dialog = dialog;
        return await dialog.Closed;
    }

    /// <summary>显示错误：面向用户的错误显示其标题和说明；其他异常显示通用说明和技术细节。</summary>
    public void ShowError(Exception ex)
    {
        var dialog = ex is UserFacingException user
            ? new MessageDialogViewModel(user.Title, user.Message, user.InnerException?.Message, isError: true)
            : new MessageDialogViewModel("出现了意外错误", "操作没有完成。如果问题反复出现，请把下面的信息发给开发者。", ex.ToString(), isError: true);
        _ = ShowDialogAsync(dialog);
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
        await Task.Delay(TimeSpan.FromSeconds(4));
        Hints.Remove(hint);
    }
}

/// <summary>尚未实现的页面。</summary>
public sealed record PlaceholderPageViewModel(string Title, string Description, FCP.UI.Controls.BlockKind Kind = FCP.UI.Controls.BlockKind.Vanilla);

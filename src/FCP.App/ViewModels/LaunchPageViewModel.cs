using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCP.UI.Controls;

namespace FCP.App.ViewModels;

/// <summary>版本的摘要信息（原型阶段为示例数据）。</summary>
public sealed record InstanceSummary(
    string Name,
    string McVersion,
    string? Loader,
    string? Account,
    BlockKind Kind,
    string LastPlayed,
    int ModCount = 0,
    bool IsFavorite = false)
{
    public string Info => string.Join(" · ", new[] { McVersion, Loader, Account }.Where(s => !string.IsNullOrEmpty(s)));

    public string ModCountText => ModCount > 0 ? $"{ModCount} 个 Mod" : "无 Mod";
}

/// <summary>
/// 启动页。原型阶段使用示例数据，启动过程为模拟进度；P1 接入真正的启动管线。
/// </summary>
public sealed partial class LaunchPageViewModel : ObservableObject
{
    private static readonly string[] Tips =
    [
        "按 Ctrl + Enter 可以直接启动当前版本。",
        "把 Mod 文件拖进窗口，就能安装到当前版本。",
        "版本设置中的每一项都可以选择“跟随全局设置”，只在需要时单独调整。",
        "游戏崩溃后，启动器会分析日志，并用大白话告诉你原因和解决办法。",
        "在版本选择页按 F11，可以显示被隐藏的版本。",
        "内存选择“自动配置”时，会根据 Mod 数量和电脑的剩余内存来分配。",
    ];

    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _launchCts;

    public LaunchPageViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Recent =
        [
            new InstanceSummary("FarmingTales_ForgeⅡ v1.4.3", "1.20.1", "Forge 47.4.10", "LittleSkin", BlockKind.Forge, "3 天前", 320, true),
            new InstanceSummary("香草纪元：食旅纪行", "1.20.1", "Forge 47.3.33", "LittleSkin", BlockKind.Forge, "5 月 8 日", 408),
        ];
        _selectedInstance = Recent[0];
        InstanceSelect = new InstanceSelectViewModel(shell, this);
        Tip = Tips[Random.Shared.Next(Tips.Length)];
    }

    public InstanceSelectViewModel InstanceSelect { get; }

    public ObservableCollection<InstanceSummary> Recent { get; }

    public string Greeting => DateTime.Now.Hour switch
    {
        >= 5 and < 11 => "早上好",
        >= 11 and < 13 => "中午好",
        >= 13 and < 18 => "下午好",
        _ => "晚上好",
    };

    public string Tip { get; }

    public string StatsText => $"{Recent.Count} 个版本 · {Recent.Sum(r => r.ModCount)} 个 Mod";

    [ObservableProperty]
    private string _playerName = "Steve";

    [ObservableProperty]
    private string _accountType = "离线账号";

    [ObservableProperty]
    private InstanceSummary _selectedInstance;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private double _launchProgress;

    [ObservableProperty]
    private string _launchStage = "";

    public void SelectInstance(InstanceSummary instance) => SelectedInstance = instance;

    [RelayCommand]
    private void OpenInstanceSelect() => _shell.OpenSubPage(InstanceSelect, "版本选择");

    [RelayCommand]
    private void OpenInstanceSettings() =>
        _shell.OpenSubPage(new PlaceholderPageViewModel("版本设置", "概览、设置、Mod、资源包、光影和存档管理将在 P2 实现。", BlockKind.Forge), $"版本设置 - {SelectedInstance.Name}");

    [RelayCommand]
    private void SwitchAccount() => _shell.ShowHint("账号管理将在 P2 实现（原型）");

    [RelayCommand]
    private void LaunchRecent(InstanceSummary instance)
    {
        SelectedInstance = instance;
        LaunchCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (IsLaunching)
            return;
        _launchCts = new CancellationTokenSource();
        IsLaunching = true;
        LaunchProgress = 0;
        string[] stages = ["检查 Java", "登录", "补全文件", "构建启动参数", "启动进程", "等待游戏窗口"];
        try
        {
            for (var i = 0; i < stages.Length; i++)
            {
                LaunchStage = stages[i];
                for (var step = 0; step < 10; step++)
                {
                    await Task.Delay(40, _launchCts.Token);
                    LaunchProgress = (i * 10 + step + 1) * 100.0 / (stages.Length * 10);
                }
            }
            _shell.ShowHint($"{SelectedInstance.Name} 启动成功（原型演示）", HintKind.Success);
        }
        catch (OperationCanceledException)
        {
            _shell.ShowHint("已取消启动");
        }
        finally
        {
            IsLaunching = false;
            _launchCts.Dispose();
            _launchCts = null;
        }
    }

    [RelayCommand]
    private void CancelLaunch() => _launchCts?.Cancel();
}

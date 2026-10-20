using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FCP.App.ViewModels;

public sealed record InstanceSummary(string Name, string Info, string LastPlayed);

/// <summary>
/// 启动页。原型阶段使用示例数据，启动过程为模拟进度；P1 接入真正的启动管线。
/// </summary>
public sealed partial class LaunchPageViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _launchCts;

    public LaunchPageViewModel(ShellViewModel shell)
    {
        _shell = shell;
        InstanceSelect = new InstanceSelectViewModel(shell, this);
        Recent =
        [
            new InstanceSummary("FarmingTales_ForgeⅡ v1.4.3", "1.20.1 · Forge 47.4.10 · LittleSkin", "3 天前"),
            new InstanceSummary("香草纪元：食旅纪行", "1.20.1 · Forge 47.3.33 · LittleSkin", "5 月 8 日"),
        ];
        SelectedInstance = Recent[0];
    }

    public InstanceSelectViewModel InstanceSelect { get; }

    public ObservableCollection<InstanceSummary> Recent { get; }

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
        _shell.OpenSubPage(new PlaceholderPageViewModel("版本设置", "概览、设置、Mod、资源包、光影和存档管理将在 P2 实现。"), $"版本设置 - {SelectedInstance.Name}");

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

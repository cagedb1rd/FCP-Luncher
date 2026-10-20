using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCP.Core;
using FCP.Core.Launch;
using FCP.Core.Platform;
using FCP.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FCP.App.ViewModels;

/// <summary>启动页：账号、当前版本、启动按钮与启动进度、主页（继续游玩、小贴士）。</summary>
public sealed partial class LaunchPageViewModel : ObservableObject
{
    private static readonly string[] Tips =
    [
        "按 Ctrl + Enter 可以直接启动当前版本。",
        "版本设置中的每一项都可以选择“跟随全局设置”，只在需要时单独调整。",
        "在版本选择页右键一个版本，可以创建桌面快捷方式，双击直接进入游戏。",
        "启动器会记录每个版本的游玩时长，看看你在哪个整合包里花的时间最多。",
        "新安装的 Java 会被自动发现，不需要手动添加。",
        "内存选择“自动配置”时，会根据 Mod 数量和电脑的剩余内存来分配。",
    ];

    private readonly ShellViewModel _shell;
    private readonly LaunchService _launchService;
    private readonly SettingsStore _settingsStore;
    private readonly IShell _platformShell;
    private readonly ILogger<LaunchPageViewModel> _logger;
    private CancellationTokenSource? _launchCts;
    private GameSession? _session;
    private readonly System.Windows.Threading.DispatcherTimer _runningTimer;

    public LaunchPageViewModel(ShellViewModel shell, LauncherState state, LaunchService launchService, SettingsStore settingsStore, IShell platformShell, ILogger<LaunchPageViewModel> logger)
    {
        _shell = shell;
        State = state;
        _launchService = launchService;
        _settingsStore = settingsStore;
        _platformShell = platformShell;
        _logger = logger;
        Tip = Tips[Random.Shared.Next(Tips.Length)];
        _runningTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runningTimer.Tick += (_, _) => OnPropertyChanged(nameof(RunningText));
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LauncherState.SelectedInstance) or nameof(LauncherState.IsLoading) or nameof(LauncherState.PclImport))
            {
                RefreshHome();
                OnPropertyChanged(nameof(LaunchButtonText));
                LaunchCommand.NotifyCanExecuteChanged();
            }
            else if (e.PropertyName is nameof(LauncherState.Account))
            {
                OnPropertyChanged(nameof(GreetingText));
            }
        };
        state.Instances.CollectionChanged += (_, _) => RefreshHome();
    }

    public LauncherState State { get; }

    public string Greeting => DateTime.Now.Hour switch
    {
        >= 5 and < 11 => "早上好",
        >= 11 and < 13 => "中午好",
        >= 13 and < 18 => "下午好",
        _ => "晚上好",
    };

    public string GreetingText => State.Account is null ? $"{Greeting}！" : $"{Greeting}，{State.PlayerName}";

    public string Tip { get; }

    /// <summary>继续游玩：最近玩过的版本（最多 4 个）。</summary>
    public IReadOnlyList<InstanceItem> Recent => State.Instances
        .Where(i => i.IsHealthy && i.Entry.Group != FCP.Core.Instances.InstanceGroup.Hidden)
        .OrderByDescending(i => i.Entry.LastPlayed ?? DateTimeOffset.MinValue)
        .Take(4)
        .ToList();

    public bool HasRecent => Recent.Count > 0;

    public string StatsText => State.IsLoading ? "正在查找你的游戏…" : $"{State.Instances.Count} 个版本 · {State.Instances.Sum(i => i.ModCount)} 个 Mod";

    /// <summary>主页右下角的卡片：PCL 迁移用户看到接管情况（沿用了什么、还需要确认什么），其他用户看到游戏文件夹概况。</summary>
    public string FoldersTitle => State.PclImport is not null || State.HasPcl ? "已接管 PCL 中的游戏" : "你的游戏文件夹";

    public string FoldersText => State.IsLoading
        ? "正在查找游戏文件夹…"
        : State.PclImport is { } import
            ? $"找到 {State.Folders.Count} 个游戏文件夹，当前文件夹有 {State.Instances.Count} 个版本。PCL 的全局设置和每个版本的设置都已沿用，PCL 的文件不会被修改。"
            : State.Folders.Count == 0
                ? "FCP 会启动电脑上已有的游戏。点击“版本选择”添加 .minecraft 文件夹。"
                : $"找到 {State.Folders.Count} 个游戏文件夹，当前文件夹有 {State.Instances.Count} 个版本。在“版本选择”中可以添加其他文件夹。";

    public bool HasMigrationDetails => State.PclImport is not null;

    public string? MigrationNotesText => State.PclImport is { Notes.Count: > 0 } import ? $"有 {import.Notes.Count} 件事需要你确认" : null;

    [RelayCommand]
    private Task ShowMigrationDetailsAsync()
    {
        if (State.PclImport is not { } import)
            return Task.CompletedTask;
        var lines = new List<string> { "已沿用：" };
        lines.AddRange(import.Items.Select(i => "  ✔ " + i));
        lines.Add("  ✔ 每个版本在 PCL 中的设置（内存、版本隔离、参数、Java、自动进入的服务器等），在 FCP 中没有单独修改过的项都会继续生效");
        if (import.Notes.Count > 0)
        {
            lines.Add("");
            lines.Add("需要你确认：");
            lines.AddRange(import.Notes.Select(n => "  ● " + n));
        }
        var dialog = new MessageDialogViewModel(
            "从 PCL 沿用的内容",
            $"来自 {import.SourceDirectory}（{import.ImportedAt:yyyy-MM-dd HH:mm}）。PCL 的文件没有被修改，两个启动器可以交替使用；在 FCP 中修改的设置不会同步回 PCL。",
            string.Join(Environment.NewLine, lines))
        { DetailsMonospace = false };
        return _shell.ShowDialogAsync(dialog);
    }

    private void RefreshHome()
    {
        OnPropertyChanged(nameof(Recent));
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(FoldersTitle));
        OnPropertyChanged(nameof(FoldersText));
        OnPropertyChanged(nameof(HasMigrationDetails));
        OnPropertyChanged(nameof(MigrationNotesText));
    }

    public string LaunchButtonText => State.IsLoading ? "正在加载" : State.SelectedInstance is null ? "添加游戏文件夹" : "启动游戏";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    private bool _isLaunching;

    [ObservableProperty]
    private double _launchProgress;

    [ObservableProperty]
    private string _launchStage = "";

    [ObservableProperty]
    private string? _launchDetail;

    /// <summary>本次启动使用的 Java 和选择理由（PCL 没有的功能：让用户知道为什么用这个 Java）。</summary>
    [ObservableProperty]
    private string? _launchJavaText;

    [ObservableProperty]
    private string? _launchJavaReason;

    [ObservableProperty]
    private string? _launchMemoryText;

    [ObservableProperty]
    private string? _launchMemoryReason;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunningText))]
    private bool _isGameRunning;

    public string RunningText => _session is { } s ? $"游戏运行中 · {(DateTimeOffset.Now - s.StartedAt):mm\\:ss}" : "";

    private bool CanLaunch() => !IsLaunching && !State.IsLoading;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (State.SelectedInstance is null)
        {
            await _shell.InstanceSelect.AddFolderCommand.ExecuteAsync(null);
            return;
        }
        if (State.Account is null)
        {
            await _shell.ShowDialogAsync(new AccountDialogViewModel(State));
            if (State.Account is null)
                return;
        }
        await LaunchInstanceAsync(State.SelectedInstance);
    }

    public async Task LaunchInstanceAsync(InstanceItem item)
    {
        if (IsLaunching || State.Account is not { } account)
            return;
        _launchCts = new CancellationTokenSource();
        IsLaunching = true;
        LaunchProgress = 0;
        LaunchStage = "准备中";
        LaunchDetail = null;
        LaunchJavaText = LaunchJavaReason = LaunchMemoryText = LaunchMemoryReason = null;
        var progress = new Progress<LaunchProgress>(p =>
        {
            LaunchStage = p.Stage;
            LaunchProgress = p.Percent;
            LaunchDetail = p.Detail;
        });
        try
        {
            var prepared = await _launchService.PrepareAsync(item.Entry, account, new LaunchOptions(), progress, _launchCts.Token);
            LaunchJavaText = prepared.Java.DisplayName;
            LaunchJavaReason = prepared.JavaExplanation;
            LaunchMemoryText = $"{prepared.MemoryMb / 1024.0:0.#} GB";
            LaunchMemoryReason = prepared.MemoryExplanation;
            var session = await _launchService.StartAsync(prepared, progress, _launchCts.Token);
            AttachSession(session, item);

            // 等待游戏窗口出现（期间仍可取消）；游戏在窗口出现前就退出时由 WatchExitAsync 处理
            await Task.WhenAny(session.WindowShownTask, session.Completion).WaitAsync(_launchCts.Token);
            if (!session.Completion.IsCompleted)
            {
                LaunchProgress = 100;
                _shell.ShowHint($"{item.Name} 已启动", HintKind.Success);
                await ApplyVisibilityOnStartAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _session?.Kill();
            _shell.ShowHint("已取消启动");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动失败");
            _shell.ShowError(ex);
        }
        finally
        {
            IsLaunching = false;
            _launchCts?.Dispose();
            _launchCts = null;
        }
    }

    private void AttachSession(GameSession session, InstanceItem item)
    {
        _session = session;
        IsGameRunning = true;
        _runningTimer.Start();
        _ = WatchExitAsync(session, item);
    }

    /// <summary>在界面线程上等待游戏退出：刷新游玩记录、恢复启动器窗口、崩溃时提示。</summary>
    private async Task WatchExitAsync(GameSession session, InstanceItem item)
    {
        var exit = await session.Completion;
        try
        {
            if (ReferenceEquals(_session, session))
            {
                _runningTimer.Stop();
                IsGameRunning = false;
                _session = null;
            }
            session.Dispose();
            await State.RefreshInstanceAsync(item);
            RefreshHome();
            await ApplyVisibilityOnExitAsync();
            if (exit.IsCrash)
                await ShowCrashAsync(item, exit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理游戏退出失败");
        }
    }

    private async Task ShowCrashAsync(InstanceItem item, GameExit exit)
    {
        var logDir = Path.GetDirectoryName(exit.LogFile)!;
        var dialog = new MessageDialogViewModel(
            "游戏异常退出",
            $"{item.Name} 运行 {exit.Duration:mm\\:ss} 后退出了（退出码 {exit.ExitCode}）。下面是游戏最后输出的内容；详细的崩溃分析将在下一阶段推出。",
            string.Join(Environment.NewLine, exit.LastLines.TakeLast(40)),
            isError: true,
            new DialogButton("打开日志文件夹", false, () => _platformShell.OpenFolder(logDir)),
            new DialogButton("知道了", true));
        await _shell.ShowDialogAsync(dialog);
    }

    private async Task ApplyVisibilityOnStartAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        var window = Application.Current?.MainWindow;
        if (window is null)
            return;
        switch (settings.Visibility)
        {
            case LauncherVisibility.Minimize:
                window.WindowState = WindowState.Minimized;
                break;
            case LauncherVisibility.HideAndRestore or LauncherVisibility.HideAndExit:
                window.Hide();
                break;
            case LauncherVisibility.Exit:
                Application.Current?.Shutdown();
                break;
        }
    }

    private async Task ApplyVisibilityOnExitAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        var window = Application.Current?.MainWindow;
        if (window is null)
            return;
        switch (settings.Visibility)
        {
            case LauncherVisibility.HideAndRestore:
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
                break;
            case LauncherVisibility.HideAndExit:
                Application.Current?.Shutdown();
                break;
        }
    }

    /// <summary>截图模式用：显示启动中的界面，但不真的启动。</summary>
    public void ShowLaunchingPreview(bool stop = false)
    {
        IsLaunching = !stop;
        LaunchStage = "补全文件";
        LaunchProgress = 46;
        LaunchDetail = "1208/2611";
        LaunchJavaText = "Java 17 (Oracle)";
        LaunchJavaReason = "需要 Java 17";
        LaunchMemoryText = "4.5 GB";
    }

    [RelayCommand]
    private void CancelLaunch() => _launchCts?.Cancel();

    [RelayCommand]
    private void StopGame()
    {
        _session?.Kill();
        _shell.ShowHint("已结束游戏");
    }

    [RelayCommand]
    private void OpenInstanceSelect() => _shell.OpenSubPage(_shell.InstanceSelect, "版本选择");

    [RelayCommand]
    private void OpenInstanceSettings()
    {
        if (State.SelectedInstance is { } item)
            _shell.OpenInstanceSettings(item);
    }

    [RelayCommand]
    private Task SwitchAccountAsync() => _shell.ShowDialogAsync(new AccountDialogViewModel(State));

    [RelayCommand]
    private async Task LaunchRecentAsync(InstanceItem item)
    {
        await State.SelectInstanceAsync(item);
        await LaunchAsync();
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using FCP.App.ViewModels;
using FCP.UI.Interop;

namespace FCP.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly string? _launchTarget;
    private readonly string? _snapshotDirectory = Environment.GetEnvironmentVariable("FCP_SNAPSHOT_DIR");

    public MainWindow(IServiceProvider services, string? launchTarget)
    {
        InitializeComponent();
        _launchTarget = launchTarget;
        _shell = new ShellViewModel(services);
        DataContext = _shell;
        _shell.PropertyChanged += OnShellPropertyChanged;
        SourceInitialized += (_, _) => WindowEffects.TryEnableRoundedCorners(this);
        ContentRendered += OnContentRendered;
        PreviewKeyDown += OnPreviewKeyDown;

        if (!string.IsNullOrEmpty(_snapshotDirectory))
        {
            // 截图模式：窗口放在屏幕外渲染，不抢焦点，也不出现在任务栏
            // 注意：(-32000, -32000) 是 Windows 表示“最小化窗口”的特殊坐标，窗口放在那里不会渲染
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.VirtualScreenLeft - Width - 200;
            Top = SystemParameters.VirtualScreenTop;
            ShowActivated = false;
            ShowInTaskbar = false;
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSubPage))
            AnimateTitleBar(_shell.IsSubPage);
    }

    /// <summary>进入子页面：标签组淡出并左移，“返回 + 标题”从左侧滑入；返回时相反。</summary>
    private void AnimateTitleBar(bool toSubPage)
    {
        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        void Animate(FrameworkElement element, double opacity, double x)
        {
            element.IsHitTestVisible = opacity > 0;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
            ((TranslateTransform)element.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration) { EasingFunction = ease });
        }

        Animate(TabBar, toSubPage ? 0 : 1, toSubPage ? -24 : 0);
        Animate(SubHeader, toSubPage ? 1 : 0, toSubPage ? 0 : -16);
        Logo.BeginAnimation(OpacityProperty, new DoubleAnimation(toSubPage ? 0 : 1, duration));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shell.Dialog is { } dialog)
        {
            if (e.Key == Key.Escape)
            {
                dialog.Dismiss();
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.Escape && _shell.IsSubPage)
        {
            _shell.BackCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && _shell.CurrentPage is LaunchPageViewModel launch)
        {
            launch.LaunchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F11 && _shell.CurrentPage is InstanceSelectViewModel select)
        {
            select.ShowHidden = !select.ShowHidden;
            e.Handled = true;
        }
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        // 启动耗时测量（CI 冒烟测试）：首帧渲染后写入“进程启动 → 首帧”的毫秒数并退出
        if (Environment.GetEnvironmentVariable("FCP_MEASURE_STARTUP") is { Length: > 0 } measureFile)
        {
            var elapsed = DateTime.Now - Process.GetCurrentProcess().StartTime;
            File.AppendAllText(measureFile, $"{elapsed.TotalMilliseconds:F0}{Environment.NewLine}");
            Application.Current.Shutdown();
            return;
        }

        await _shell.InitializeAsync();

        if (!string.IsNullOrEmpty(_snapshotDirectory))
        {
            await RunSnapshotTourAsync(_snapshotDirectory);
            return;
        }
        if (!string.IsNullOrEmpty(_launchTarget))
        {
            if (await _shell.State.SelectByVersionDirectoryAsync(_launchTarget))
                await _shell.Launch.LaunchCommand.ExecuteAsync(null);
            else
                _shell.ShowHint($"找不到快捷方式指向的版本：{_launchTarget}", HintKind.Error);
        }
    }

    /// <summary>
    /// 截图模式（FCP_SNAPSHOT_DIR）：依次打开各个页面，用 RenderTargetBitmap 直接渲染界面并保存为 PNG。
    /// 不截取屏幕，因此不受其他窗口遮挡影响，也不会拍到用户屏幕上的其他内容；也不会真的启动游戏。
    /// </summary>
    private async Task RunSnapshotTourAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var settle = TimeSpan.FromMilliseconds(1000);

        await Task.Delay(settle);
        SaveSnapshot(directory, "01-launch");

        _shell.Launch.OpenInstanceSelectCommand.Execute(null);
        await Task.Delay(settle);
        SaveSnapshot(directory, "02-instance-select");
        _shell.BackCommand.Execute(null);

        _shell.SelectedTab = MainTab.Settings;
        await Task.Delay(settle);
        SaveSnapshot(directory, "03-settings");
        _shell.Settings.Section = SettingsSection.Download;
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        SaveSnapshot(directory, "03b-settings-download");
        _shell.Settings.Section = SettingsSection.Other;
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        SaveSnapshot(directory, "03c-settings-other");
        _shell.Settings.Section = SettingsSection.Launch;

        _shell.SelectedTab = MainTab.Launch;
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        _shell.Launch.ShowLaunchingPreview();
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        SaveSnapshot(directory, "04-launching");
        _shell.Launch.ShowLaunchingPreview(stop: true);

        var accountDialog = new AccountDialogViewModel(_shell.State);
        _ = _shell.ShowDialogAsync(accountDialog);
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        SaveSnapshot(directory, "05-account-dialog");
        accountDialog.CancelCommand.Execute(null);

        if (_shell.Launch.ShowMigrationDetailsCommand.CanExecute(null) && _shell.State.PclImport is not null)
        {
            _ = _shell.Launch.ShowMigrationDetailsCommand.ExecuteAsync(null);
            await Task.Delay(TimeSpan.FromMilliseconds(700));
            SaveSnapshot(directory, "05b-migration-details");
            _shell.Dialog?.Dismiss();
        }

        if (_shell.State.SelectedInstance is { } instance)
        {
            _shell.OpenInstanceSettings(instance);
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
            SaveSnapshot(directory, "07-instance-overview");
            if (_shell.CurrentPage is InstanceSettingsViewModel page)
            {
                page.Section = InstanceSection.Settings;
                await Task.Delay(TimeSpan.FromMilliseconds(600));
                SaveSnapshot(directory, "07b-instance-settings");
                page.Section = InstanceSection.Mods;
                await Task.Delay(TimeSpan.FromMilliseconds(4000));
                SaveSnapshot(directory, "07c-instance-mods");
                page.Section = InstanceSection.Saves;
                await Task.Delay(TimeSpan.FromMilliseconds(800));
                SaveSnapshot(directory, "07d-instance-saves");
            }
            _shell.BackCommand.Execute(null);
        }

        _shell.SelectedTab = MainTab.More;
        await Task.Delay(settle);
        SaveSnapshot(directory, "06-placeholder");

        Application.Current.Shutdown();
    }

    private void SaveSnapshot(string directory, string name)
    {
        var dpi = VisualTreeHelper.GetDpi(Root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(Root.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(Root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(Root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

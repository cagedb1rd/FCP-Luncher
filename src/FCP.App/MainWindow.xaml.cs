using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FCP.App.ViewModels;
using FCP.UI.Interop;

namespace FCP.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow()
    {
        InitializeComponent();
        _shell = new ShellViewModel();
        DataContext = _shell;
        _shell.PropertyChanged += OnShellPropertyChanged;
        SourceInitialized += (_, _) => WindowEffects.TryEnableRoundedCorners(this);
        ContentRendered += OnContentRendered;
        PreviewKeyDown += OnPreviewKeyDown;
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
        if (e.Key == Key.Escape && _shell.IsSubPage)
        {
            _shell.BackCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 启动耗时测量（P0 Spike B）：设置环境变量 FCP_MEASURE_STARTUP=&lt;文件路径&gt; 时，
    /// 首帧渲染后把“进程启动 → 首帧”的毫秒数追加到该文件，然后退出。
    /// </summary>
    private void OnContentRendered(object? sender, EventArgs e)
    {
        var measureFile = Environment.GetEnvironmentVariable("FCP_MEASURE_STARTUP");
        if (string.IsNullOrEmpty(measureFile))
            return;
        var elapsed = DateTime.Now - Process.GetCurrentProcess().StartTime;
        File.AppendAllText(measureFile, $"{elapsed.TotalMilliseconds:F0}{Environment.NewLine}");
        Application.Current.Shutdown();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

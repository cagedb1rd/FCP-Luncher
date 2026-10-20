using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using FCP.Core.Java;
using FCP.Hosting;

namespace FCP.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // 未处理的异常：写入崩溃日志并提示，而不是静默退出
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception);
        base.OnStartup(e);

        // 命令行：--launch "<版本目录>"（桌面快捷方式使用）
        string? launchTarget = null;
        for (var i = 0; i < e.Args.Length - 1; i++)
        {
            if (e.Args[i] == "--launch")
                launchTarget = e.Args[i + 1];
        }

        var services = await FcpServices.CreateAsync(AppContext.BaseDirectory);
        var window = new MainWindow(services, launchTarget);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logFile = WriteCrashLog(e.Exception);
        e.Handled = true;
        if (Environment.GetEnvironmentVariable("FCP_MEASURE_STARTUP") is { Length: > 0 } measureFile)
        {
            File.AppendAllText(measureFile, $"CRASH {e.Exception.GetType().Name}: {e.Exception.Message}{Environment.NewLine}");
            Shutdown(1);
            return;
        }
        MessageBox.Show($"启动器遇到了意外错误，即将关闭。\n\n{e.Exception.Message}\n\n错误日志：{logFile}", "FCP", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private static string? WriteCrashLog(Exception? exception)
    {
        try
        {
            var dir = Environment.GetEnvironmentVariable("FCP_CONFIG_DIR") is { Length: > 0 } config
                ? Path.Combine(config, "logs")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FCP", "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, $"{DateTime.Now:O}{Environment.NewLine}{exception}");
            return file;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

/// <summary>
/// 图片路径 → 图片。一次性读入内存（OnLoad），不占用文件：
/// 存档的 icon.png 会被游戏改写，图片被锁住会导致游戏保存失败。
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 64;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Java 来源 → 中文说明。</summary>
public sealed class JavaSourceTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        JavaSource.Registry => "注册表",
        JavaSource.KnownFolder => "安装目录",
        JavaSource.MinecraftRuntime => "官方启动器",
        JavaSource.OtherLauncher => "其他启动器",
        JavaSource.PathVariable => "PATH",
        JavaSource.JavaHome => "JAVA_HOME",
        JavaSource.Pcl => "PCL 记录",
        JavaSource.Downloaded => "自动下载",
        JavaSource.User => "手动添加",
        _ => "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

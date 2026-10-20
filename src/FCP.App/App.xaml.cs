using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FCP.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 未处理的异常：写入崩溃日志并提示，而不是静默退出（P1 接入 Serilog 后统一处理）
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception);
        base.OnStartup(e);
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
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FCP", "logs");
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

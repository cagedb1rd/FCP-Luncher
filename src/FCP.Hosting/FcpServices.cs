using System.Net;
using FCP.Core;
using FCP.Core.Accounts;
using FCP.Core.Folders;
using FCP.Core.Instances;
using FCP.Core.Java;
using FCP.Core.Launch;
using FCP.Core.Migration;
using FCP.Core.Net;
using FCP.Core.Platform;
using FCP.Core.Settings;
using FCP.Core.Storage;
using FCP.Engine.CmlLib;
using FCP.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FCP.Hosting;

/// <summary>创建服务容器（App 与 CLI 共用）。</summary>
public static class FcpServices
{
    public static async Task<ServiceProvider> CreateAsync(string executableDirectory, CancellationToken cancellationToken = default)
    {
        var paths = await ResolvePathsAsync(executableDirectory, cancellationToken).ConfigureAwait(false);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.LogsDirectory, "launcher-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(loggerConfig.CreateLogger(), dispose: true));
        services.AddSingleton(paths);

        // 平台
        services.AddSingleton<IRegistryReader, WindowsRegistryReader>();
        services.AddSingleton<IShell, WindowsShell>();
        services.AddSingleton<IWindowLocator, WindowsWindowLocator>();
        services.AddSingleton<ISystemInfo, WindowsSystemInfo>();
        services.AddSingleton<IProtectedData, DpapiProtectedData>();

        // 网络：官方源 / BMCLAPI 镜像自动选择与失败回退
        services.AddSingleton(sp =>
        {
            var handler = new MirrorHandler(sp.GetRequiredService<SettingsStore>(), sp.GetRequiredService<ILogger<MirrorHandler>>())
            {
                InnerHandler = new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    ConnectTimeout = TimeSpan.FromSeconds(15),
                },
            };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"FCP/{typeof(FcpServices).Assembly.GetName().Version?.ToString(3) ?? "0"}");
            return http;
        });

        // 核心服务
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<FolderSettingsStore>();
        services.AddSingleton<GameFolderDiscovery>();
        services.AddSingleton<InstanceScanner>();
        services.AddSingleton<InstanceCatalog>();
        services.AddSingleton<JavaDiscovery>();
        services.AddSingleton<JavaProbe>();
        services.AddSingleton<JavaManager>();
        services.AddSingleton<JavaRuntimeDownloader>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<PclMigrationService>();
        services.AddSingleton<ILaunchEngine, CmlLibLaunchEngine>();
        services.AddSingleton<LaunchService>();
        return services.BuildServiceProvider();
    }

    /// <summary>确定数据目录：设置中已有就沿用；首次运行按 D-014 的规则确定并保存。</summary>
    private static async Task<AppPaths> ResolvePathsAsync(string executableDirectory, CancellationToken cancellationToken)
    {
        var bootstrap = AppPaths.Resolve(executableDirectory);
        if (bootstrap.IsPortable)
            return bootstrap;
        var settings = await JsonFile.ReadAsync<GlobalSettings>(bootstrap.SettingsFile, cancellationToken).ConfigureAwait(false) ?? new GlobalSettings();
        if (settings.DataDirectory is null && Environment.GetEnvironmentVariable("FCP_DATA_DIR") is not { Length: > 0 })
        {
            settings.DataDirectory = bootstrap.DataDirectory;
            await JsonFile.WriteAsync(bootstrap.SettingsFile, settings, cancellationToken).ConfigureAwait(false);
        }
        return AppPaths.Resolve(executableDirectory, settings.DataDirectory);
    }
}

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FCP.Core.Platform;
using Microsoft.Extensions.Logging;

namespace FCP.Core.Java;

/// <summary>
/// 发现电脑上的 Java（docs/04 §5.3）。比 PCL 多读取注册表和 PATH，并且每次打开都可以快速重新扫描。
/// </summary>
public sealed class JavaDiscovery(IRegistryReader registry, ILogger<JavaDiscovery> logger)
{
    // 注册表中保存 Java 安装路径的位置（各发行版的安装程序写入的位置）
    private static readonly string[] RegistryRoots =
    [
        @"SOFTWARE\JavaSoft\JDK",
        @"SOFTWARE\JavaSoft\JRE",
        @"SOFTWARE\JavaSoft\Java Development Kit",
        @"SOFTWARE\JavaSoft\Java Runtime Environment",
        @"SOFTWARE\Eclipse Adoptium",
        @"SOFTWARE\Eclipse Foundation",
        @"SOFTWARE\AdoptOpenJDK",
        @"SOFTWARE\Azul Systems\Zulu",
        @"SOFTWARE\Microsoft\JDK",
        @"SOFTWARE\BellSoft",
        @"SOFTWARE\Amazon Corretto",
        @"SOFTWARE\Semeru",
    ];

    private static readonly string[] RegistryValueNames = ["JavaHome", "Path", "InstallationPath", "InstallLocation"];

    private static readonly string[] KnownVendorFolders =
    [
        "Java", "Eclipse Adoptium", "Eclipse Foundation", "AdoptOpenJDK", "Zulu", "Microsoft", "Amazon Corretto", "BellSoft", "Semeru", "OpenJDK",
    ];

    /// <summary>搜索所有来源，返回候选 java.exe（未探测）及其来源，按路径去重。</summary>
    public IReadOnlyList<(string JavaExe, JavaSource Source)> FindCandidates(IEnumerable<string>? extraRuntimeRoots = null)
    {
        var results = new List<(string, JavaSource)>();
        void AddHome(string? home, JavaSource source)
        {
            if (string.IsNullOrWhiteSpace(home))
                return;
            try
            {
                var exe = Path.Combine(home.Trim().Trim('"'), "bin", "java.exe");
                AddExe(exe, source);
            }
            catch (ArgumentException)
            {
            }
        }
        void AddExe(string exe, JavaSource source)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                // Oracle 安装程序在 PATH 中放的 javapath 只是转发程序，真正的 Java 会通过注册表找到
                if (full.Contains(@"\Oracle\Java\javapath", StringComparison.OrdinalIgnoreCase))
                    return;
                if (File.Exists(full) && !results.Any(r => string.Equals(r.Item1, full, StringComparison.OrdinalIgnoreCase)))
                    results.Add((full, source));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
            }
        }

        // 1. 注册表（64 位和 32 位视图都看）
        foreach (var root in RegistryRoots)
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (var view32 in new[] { false, true })
                {
                    foreach (var home in ReadRegistryHomes(hive, root, view32, depth: 0))
                        AddHome(home, JavaSource.Registry);
                }
            }
        }

        // 2. 常见安装目录
        foreach (var programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Where(p => !string.IsNullOrEmpty(p)).Distinct())
        {
            foreach (var vendor in KnownVendorFolders)
            {
                var dir = Path.Combine(programFiles, vendor);
                foreach (var home in SafeSubdirectories(dir))
                    AddHome(home, JavaSource.KnownFolder);
            }
        }

        // 3. 官方启动器和其他启动器的运行时（在其中递归查找 java.exe）
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        (string Dir, JavaSource Source)[] runtimeRoots =
        [
            (Path.Combine(appData, ".minecraft", "runtime"), JavaSource.MinecraftRuntime),
            (Path.Combine(localAppData, "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime"), JavaSource.MinecraftRuntime),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "runtime"), JavaSource.MinecraftRuntime),
            (Path.Combine(appData, ".hmcl", "java"), JavaSource.OtherLauncher),
            (Path.Combine(appData, "PrismLauncher", "java"), JavaSource.OtherLauncher),
            (Path.Combine(appData, "ModrinthApp", "meta", "java_versions"), JavaSource.OtherLauncher),
            (Path.Combine(userProfile, "curseforge", "minecraft", "Install", "runtime"), JavaSource.OtherLauncher),
            (Path.Combine(userProfile, ".jdks"), JavaSource.OtherLauncher),
        ];
        foreach (var (dir, source) in runtimeRoots)
        {
            foreach (var exe in FindJavaExecutables(dir, maxDepth: 5))
                AddExe(exe, source);
        }
        foreach (var dir in extraRuntimeRoots ?? [])
        {
            foreach (var exe in FindJavaExecutables(dir, maxDepth: 5))
                AddExe(exe, JavaSource.Downloaded);
        }

        // 4. 环境变量
        AddHome(Environment.GetEnvironmentVariable("JAVA_HOME"), JavaSource.JavaHome);
        AddHome(Environment.GetEnvironmentVariable("JDK_HOME"), JavaSource.JavaHome);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddExe(Path.Combine(dir.Trim('"'), "java.exe"), JavaSource.PathVariable);

        // 5. PCL 记录过的 Java（只读：注册表中的旧列表 + %APPDATA%\PCL\config.json）
        foreach (var home in ReadPclJavaFolders())
            AddExe(Path.Combine(home, "java.exe"), JavaSource.Pcl);

        logger.LogInformation("找到 {Count} 个 Java 候选", results.Count);
        return results;
    }

    private IEnumerable<string> ReadRegistryHomes(RegistryHive hive, string key, bool view32, int depth)
    {
        foreach (var valueName in RegistryValueNames)
        {
            if (registry.ReadString(hive, key, valueName, view32) is { Length: > 0 } value)
                yield return value;
        }
        if (depth >= 4)
            yield break;
        foreach (var sub in registry.GetSubKeyNames(hive, key, view32))
        {
            foreach (var home in ReadRegistryHomes(hive, key + "\\" + sub, view32, depth + 1))
                yield return home;
        }
    }

    private IEnumerable<string> ReadPclJavaFolders()
    {
        var list = new List<string>();
        foreach (var key in new[] { @"Software\PCL", @"Software\PCLDebug" })
        {
            if (registry.ReadString(RegistryHive.CurrentUser, key, "LaunchArgumentJavaAll") is not { Length: > 0 } json)
                continue;
            try
            {
                foreach (var item in JsonNode.Parse(json) as JsonArray ?? [])
                {
                    if (item?["Path"]?.GetValue<string>() is { Length: > 0 } path)
                        list.Add(path);
                }
            }
            catch (JsonException)
            {
            }
        }
        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL", "config.json");
        if (File.Exists(config))
        {
            try
            {
                foreach (var item in JsonNode.Parse(File.ReadAllText(config))?["JavaList"] as JsonArray ?? [])
                {
                    if (item?["Folder"]?.GetValue<string>() is { Length: > 0 } folder)
                        list.Add(folder);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
            }
        }
        return list;
    }

    private static IEnumerable<string> SafeSubdirectories(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).ToList() : [];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> FindJavaExecutables(string dir, int maxDepth)
    {
        if (!Directory.Exists(dir))
            return [];
        try
        {
            return Directory.EnumerateFiles(dir, "java.exe", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = maxDepth,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, // 跳过符号链接，避免重复和循环
            }).Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "bin", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}

/// <summary>
/// 探测 Java 的版本与架构：优先读取 release 文件（快），读不到时运行 java -XshowSettings:properties -version。
/// </summary>
public sealed class JavaProbe(ILogger<JavaProbe> logger)
{
    public async Task<JavaRuntime?> ProbeAsync(string javaExe, JavaSource source, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(javaExe))
            return null;
        if (JavaReleaseFile.TryRead(javaExe) is { } info)
        {
            var is64 = info.Architecture is null || info.Architecture.Contains("64", StringComparison.Ordinal);
            return new JavaRuntime { JavaExecutable = javaExe, Version = info.Version, Vendor = CleanVendor(info.Vendor), Is64Bit = is64, Source = source };
        }
        return await RunProbeAsync(javaExe, source, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JavaRuntime?> RunProbeAsync(string javaExe, JavaSource source, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(javaExe)
                {
                    ArgumentList = { "-XshowSettings:properties", "-version" },
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await stderr.ConfigureAwait(false) + "\n" + await stdout.ConfigureAwait(false);

            string? Property(string name) => output.Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith(name + " = ", StringComparison.Ordinal))?[(name.Length + 3)..];

            var version = Property("java.version") is { } v ? JavaReleaseFile.ParseJavaVersion(v) : null;
            if (version is null)
                return null;
            var arch = Property("os.arch") ?? "";
            return new JavaRuntime
            {
                JavaExecutable = javaExe, Version = version, Vendor = CleanVendor(Property("java.vendor")),
                Is64Bit = arch.Contains("64", StringComparison.Ordinal), Source = source,
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or OperationCanceledException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            logger.LogWarning(ex, "探测 Java 失败：{Java}", javaExe);
            return null;
        }
    }

    private static string? CleanVendor(string? vendor) => vendor?.Trim() switch
    {
        null or "" => null,
        "N/A" => null,
        var v when v.Contains("Eclipse Adoptium", StringComparison.OrdinalIgnoreCase) => "Eclipse Adoptium",
        var v when v.Contains("Oracle", StringComparison.OrdinalIgnoreCase) => "Oracle",
        var v when v.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) => "Microsoft",
        var v when v.Contains("Azul", StringComparison.OrdinalIgnoreCase) => "Azul Zulu",
        var v when v.Contains("JetBrains", StringComparison.OrdinalIgnoreCase) => "JetBrains",
        var v when v.Contains("Amazon", StringComparison.OrdinalIgnoreCase) => "Amazon Corretto",
        var v when v.Contains("BellSoft", StringComparison.OrdinalIgnoreCase) => "BellSoft Liberica",
        var v when v.Contains("Mojang", StringComparison.OrdinalIgnoreCase) => "Mojang",
        var v => v,
    };
}

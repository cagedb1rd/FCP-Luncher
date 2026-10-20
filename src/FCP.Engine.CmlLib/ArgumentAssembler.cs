using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using FCP.Core.Launch;

namespace FCP.Engine.CmlLib;

/// <summary>
/// 组装启动参数：版本 JSON 的 JVM 参数 → 启动器参数（内存、GC、编码）→ 用户 JVM 参数 → 主类 →
/// 版本 JSON 的游戏参数 → 窗口 / 服务器 → 用户游戏参数。然后去重，并清理没有值的占位符。
/// </summary>
internal sealed partial class ArgumentAssembler
{
    // 没有值的占位符先替换成这个标记，最后把含有它的参数连同它的键一起删除，
    // 避免出现 PCL 那样把 "${clientid}" 原样传给游戏的情况。
    private const string Missing = "\u0000FCP_MISSING\u0000";

    private readonly IRulesEvaluator _rules;
    private readonly RulesEvaluatorContext _baseContext;
    private readonly MinecraftPath _path;
    private readonly IVersion _version;
    private readonly LaunchRequest _request;
    private readonly string _nativesDirectory;
    private readonly List<string> _warnings = [];

    public ArgumentAssembler(IRulesEvaluator rules, RulesEvaluatorContext baseContext, MinecraftPath path, IVersion version, LaunchRequest request, string nativesDirectory)
    {
        _rules = rules;
        _baseContext = baseContext;
        _path = path;
        _version = version;
        _request = request;
        _nativesDirectory = nativesDirectory;
    }

    public LaunchPlan Build()
    {
        var supportsQuickPlay = _version.ConcatInheritedGameArguments()
            .Any(a => a.Values.Any(v => v.Contains("${quickPlayMultiplayer}", StringComparison.Ordinal)));
        var context = BuildRulesContext(supportsQuickPlay);
        var classpath = BuildClasspath(context);
        var variables = BuildVariables(classpath);

        // ---------- JVM ----------
        var jvm = new List<string>();
        var versionJvm = _version.ConcatInheritedJvmArguments().ToList();
        jvm.AddRange(Expand(versionJvm.Count > 0 ? versionJvm : MLaunchOption.DefaultJvmArguments, context, variables));
        if (!jvm.Any(a => a.StartsWith("-Djava.library.path=", StringComparison.Ordinal)))
            jvm.Add("-Djava.library.path=" + _nativesDirectory);
        if (!jvm.Contains("-cp") && !jvm.Contains("-classpath"))
        {
            jvm.Add("-cp");
            jvm.Add(classpath);
        }
        AddLoggingConfiguration(jvm);
        jvm.AddRange(LauncherJvmArguments());
        jvm.AddRange(_request.ExtraJvmArguments.Select(a => Interpolate(a, variables)));
        jvm = ArgumentDeduplicator.DeduplicateJvm(RemoveMissing(jvm));

        var mainClass = _version.GetInheritedProperty(v => v.MainClass);
        if (string.IsNullOrEmpty(mainClass))
            throw new InvalidOperationException($"版本 {_version.Id} 的 JSON 中没有 mainClass，无法启动。");

        // ---------- 游戏 ----------
        var game = new List<string>(Expand(_version.ConcatInheritedGameArguments(), context, variables));
        if (_request.WindowWidth is int width && _request.WindowHeight is int height && !game.Contains("--width"))
        {
            game.AddRange(["--width", width.ToString(), "--height", height.ToString()]);
        }
        if (!string.IsNullOrWhiteSpace(_request.ServerAddress) && !supportsQuickPlay)
        {
            var (host, port) = SplitServerAddress(_request.ServerAddress);
            game.AddRange(["--server", host, "--port", port.ToString()]);
        }
        if (_request.FullScreen)
            game.Add("--fullscreen");
        game.AddRange(_request.ExtraGameArguments.Select(a => Interpolate(a, variables)));
        game = ArgumentDeduplicator.DeduplicateGame(RemoveMissing(game));

        List<string> arguments = [.. jvm, mainClass, .. game];
        foreach (var unresolved in arguments.SelectMany(a => PlaceholderRegex().Matches(a)).Select(m => m.Value).Distinct())
            _warnings.Add($"未识别的占位符：{unresolved}（已原样保留）");

        return new LaunchPlan(_request.Java.JavaExecutable, _path.BasePath, arguments, mainClass, _nativesDirectory)
        {
            Warnings = _warnings,
        };
    }

    private RulesEvaluatorContext BuildRulesContext(bool supportsQuickPlay)
    {
        var features = new HashSet<string>(_baseContext.Features);
        if (_request.WindowWidth is not null && _request.WindowHeight is not null)
            features.Add("has_custom_resolution");
        if (!string.IsNullOrWhiteSpace(_request.ServerAddress) && supportsQuickPlay)
            features.Add("is_quick_play_multiplayer");
        return _baseContext with { Features = features };
    }

    /// <summary>
    /// classpath：子版本在前、父版本在后，按 group:artifact(:classifier) 去重（子版本优先），最后是版本主 jar。
    /// </summary>
    private string BuildClasspath(RulesEvaluatorContext context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<string>();
        foreach (var library in _version.EnumerateToParent().SelectMany(v => v.Libraries))
        {
            if (!library.IsClientRequired || library.Artifact is null)
                continue;
            if (library.Rules.Count > 0 && !_rules.Match(library.Rules, context))
                continue;
            if (!seen.Add(LibraryIdentifier(library.Name)))
                continue;
            var file = Path.Combine(_path.Library, library.GetLibraryPath());
            if (!File.Exists(file))
                _warnings.Add($"缺少库文件：{file}");
            entries.Add(file);
        }
        var mainJar = _path.GetVersionJarPath(_version.MainJarId);
        if (!File.Exists(mainJar))
            _warnings.Add($"缺少游戏主文件：{mainJar}");
        entries.Add(mainJar);
        return string.Join(';', entries);
    }

    private static string LibraryIdentifier(string name)
    {
        try
        {
            return PackageName.Parse(name).GetIdentifier();
        }
        catch (Exception)
        {
            return name;
        }
    }

    private Dictionary<string, string> BuildVariables(string classpath)
    {
        var session = _request.Session;
        var assetId = _version.GetInheritedProperty(v => v.AssetIndex?.Id) ?? "legacy";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["library_directory"] = _path.Library,
            ["libraries_directory"] = _path.Library,
            ["natives_directory"] = _nativesDirectory,
            ["classpath_separator"] = ";",
            ["classpath"] = classpath,
            ["launcher_name"] = _request.LauncherName,
            ["launcher_version"] = _request.LauncherVersion,
            ["version_name"] = _version.Id,
            ["version_type"] = _request.VersionTypeText ?? _request.LauncherName,
            ["game_directory"] = _path.BasePath,
            ["assets_root"] = _path.Assets,
            ["assets_index_name"] = assetId,
            ["game_assets"] = _path.GetAssetLegacyPath(assetId),
            ["primary_jar"] = _path.GetVersionJarPath(_version.MainJarId),
            ["auth_player_name"] = session.PlayerName,
            ["auth_uuid"] = session.Uuid.ToString("N"),
            ["auth_access_token"] = session.AccessToken,
            ["auth_session"] = session.AccessToken,
            ["user_type"] = session.UserType,
            ["user_properties"] = "{}",
            ["auth_xuid"] = session.Xuid ?? Missing,
            ["clientid"] = session.ClientId ?? Missing,
            ["resolution_width"] = (_request.WindowWidth ?? 854).ToString(),
            ["resolution_height"] = (_request.WindowHeight ?? 480).ToString(),
            ["quickPlayMultiplayer"] = string.IsNullOrWhiteSpace(_request.ServerAddress) ? Missing : _request.ServerAddress,
            ["quickPlayPath"] = Missing,
            ["quickPlaySingleplayer"] = Missing,
            ["quickPlayRealms"] = Missing,
        };
    }

    private IEnumerable<string> Expand(IEnumerable<MArgument> arguments, RulesEvaluatorContext context, Dictionary<string, string> variables)
    {
        foreach (var argument in arguments)
        {
            if (argument.Rules.Count > 0 && !_rules.Match(argument.Rules, context))
                continue;
            foreach (var value in argument.Values)
                yield return Interpolate(value, variables);
        }
    }

    private static string Interpolate(string value, Dictionary<string, string> variables) =>
        PlaceholderRegex().Replace(value, m => variables.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private void AddLoggingConfiguration(List<string> jvm)
    {
        if (!_request.UseMojangLogConfiguration)
            return;
        var logging = _version.GetInheritedProperty(v => v.Logging);
        if (string.IsNullOrEmpty(logging?.Argument))
            return;
        var configFile = _path.GetLogConfigFilePath(logging.LogFile?.Id ?? _version.Id);
        if (File.Exists(configFile))
            jvm.Add(logging.Argument.Replace("${path}", configFile, StringComparison.Ordinal));
        else
            _warnings.Add($"缺少日志配置文件，已跳过：{configFile}");
    }

    private IEnumerable<string> LauncherJvmArguments()
    {
        var java = _request.Java.MajorVersion;
        var userArgs = _request.ExtraJvmArguments;

        if (_request.MaxMemoryMb > 0)
            yield return $"-Xmx{_request.MaxMemoryMb}m";

        // GC：用户没有自行指定时，使用官方启动器默认的 G1 参数（P1 起由 GC 策略设置决定）
        if (_request.AddDefaultGcArguments && !userArgs.Any(a => GcFlagRegex().IsMatch(a)))
        {
            yield return "-XX:+UnlockExperimentalVMOptions";
            yield return "-XX:+UseG1GC";
            yield return "-XX:G1NewSizePercent=20";
            yield return "-XX:G1ReservePercent=20";
            yield return "-XX:MaxGCPauseMillis=50";
            yield return "-XX:G1HeapRegionSize=32M";
        }

        // 崩溃时保留完整堆栈，便于崩溃分析
        yield return "-XX:-OmitStackTraceInFastThrow";
        // 旧版 Forge 在 jar 被修改过时会拒绝启动
        yield return "-Dfml.ignoreInvalidMinecraftCertificates=true";
        yield return "-Dfml.ignorePatchDiscrepancies=true";
        // Log4Shell 防护
        yield return "-Dlog4j2.formatMsgNoLookups=true";

        // 编码：启动器统一按 UTF-8 读取游戏输出
        var (stdout, stderr) = java >= 19 ? ("stdout.encoding", "stderr.encoding") : ("sun.stdout.encoding", "sun.stderr.encoding");
        yield return $"-D{stdout}=UTF-8";
        yield return $"-D{stderr}=UTF-8";
        // Java 18+ 默认 file.encoding=UTF-8（JEP 400）；老 Mod 按系统编码读写文件，COMPAT 保持旧行为
        if (java >= 18 && !userArgs.Any(a => a.StartsWith("-Dfile.encoding=", StringComparison.Ordinal)))
            yield return "-Dfile.encoding=COMPAT";
    }

    /// <summary>删除值为 <see cref="Missing"/> 的参数；如果它前面是键（以 - 开头），连同键一起删除。</summary>
    private static List<string> RemoveMissing(List<string> arguments)
    {
        var result = new List<string>(arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (i + 1 < arguments.Count && arguments[i + 1].Contains(Missing, StringComparison.Ordinal) && arguments[i].StartsWith('-') && !arguments[i].Contains(Missing, StringComparison.Ordinal))
            {
                i++;
                continue;
            }
            if (arguments[i].Contains(Missing, StringComparison.Ordinal))
                continue;
            result.Add(arguments[i]);
        }
        return result;
    }

    private static (string Host, int Port) SplitServerAddress(string address)
    {
        var trimmed = address.Trim();
        var colon = trimmed.LastIndexOf(':');
        if (colon > 0 && int.TryParse(trimmed[(colon + 1)..], out var port))
            return (trimmed[..colon], port);
        return (trimmed, 25565);
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^-XX:[+-]?Use\w+GC$")]
    private static partial Regex GcFlagRegex();
}

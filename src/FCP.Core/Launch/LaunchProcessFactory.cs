using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FCP.Core.Launch;

/// <summary>
/// 把 <see cref="LaunchPlan"/> 转换为 <see cref="ProcessStartInfo"/>。
/// Windows 命令行最长 32767 个字符，大型整合包可能超出；
/// 此时如果 Java ≥ 9，就把参数写入 Java 的 @argfile。
/// </summary>
public static class LaunchProcessFactory
{
    /// <summary>超过这个长度就改用 @argfile（给系统留出余量）。</summary>
    public const int ArgFileThreshold = 30_000;

    static LaunchProcessFactory() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static ProcessStartInfo Create(LaunchPlan plan, int javaMajorVersion, string argFileDirectory)
    {
        var startInfo = new ProcessStartInfo(plan.JavaExecutable)
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var commandLineLength = plan.JavaExecutable.Length + 3 + WindowsCommandLine.Join(plan.Arguments).Length;
        if (commandLineLength > ArgFileThreshold && javaMajorVersion >= 9)
        {
            var argFile = WriteArgFile(plan.Arguments, argFileDirectory);
            startInfo.ArgumentList.Add("@" + argFile);
        }
        else
        {
            foreach (var argument in plan.Arguments)
                startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    /// <summary>
    /// 写入 Java @argfile。每个参数都加双引号；引号内 Java 把反斜杠当作转义符，所以 \ 和 " 需要转义。
    /// Java 启动器按系统 ANSI 代码页读取文件，所以用该代码页编码。
    /// </summary>
    public static string WriteArgFile(IReadOnlyList<string> arguments, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"launch-{DateTime.Now:yyyyMMdd-HHmmss-fff}.args");
        var sb = new StringBuilder();
        foreach (var argument in arguments)
        {
            sb.Append('"')
              .Append(argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
              .Append('"')
              .Append("\r\n");
        }
        var encoding = AnsiEncoding();
        var text = sb.ToString();
        if (!CanEncode(encoding, text))
            throw new InvalidOperationException("启动参数中含有当前系统编码无法表示的字符（通常是路径或玩家名中的特殊字符），请改用简单的文件夹名后再试。");
        File.WriteAllText(path, text, encoding);
        return path;
    }

    private static Encoding AnsiEncoding()
    {
        var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static bool CanEncode(Encoding encoding, string text)
    {
        try
        {
            encoding.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}

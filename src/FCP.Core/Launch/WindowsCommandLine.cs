using System.Text;

namespace FCP.Core.Launch;

/// <summary>
/// 按 Windows C 运行库（MSVCRT / CommandLineToArgvW）的规则给参数加引号，
/// 用于计算命令行长度、显示参数和导出启动脚本。
/// 实际启动进程时使用 <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>，由 .NET 负责转义。
/// </summary>
public static class WindowsCommandLine
{
    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return argument;

        var sb = new StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                // 引号前的反斜杠需要翻倍，引号本身再转义
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }
            backslashes = 0;
        }
        // 结尾的反斜杠后面紧跟着闭合引号，需要翻倍
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary>按 MSVCRT 规则拆分命令行（Quote 的逆操作）。</summary>
    public static List<string> Split(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        var i = 0;
        while (i < commandLine.Length)
        {
            var c = commandLine[i];
            if (c == '\\')
            {
                var count = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    count++;
                    i++;
                }
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                        i++;
                    }
                }
                else
                {
                    current.Append('\\', count);
                }
                hasToken = true;
                continue;
            }
            if (c == '"')
            {
                // 引号内连续两个引号表示一个字面引号
                if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    current.Append('"');
                    i += 2;
                    continue;
                }
                inQuotes = !inQuotes;
                hasToken = true;
                i++;
                continue;
            }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                i++;
                continue;
            }
            current.Append(c);
            hasToken = true;
            i++;
        }
        if (hasToken)
            result.Add(current.ToString());
        return result;
    }
}

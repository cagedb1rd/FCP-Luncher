namespace FCP.Core.Launch;

/// <summary>
/// 启动参数去重。版本 JSON、继承链和用户自定义参数经常包含重复项；
/// 某些游戏参数重复出现时游戏会忽略它们，甚至崩溃，所以需要整理。
/// <list type="bullet">
/// <item>JVM 参数：完全相同的单个参数或键值对只保留第一个。</item>
/// <item>游戏参数：同名的键值对以后出现的为准（用户自定义参数可以覆盖版本参数），<c>--tweakClass</c> 除外（允许多个）。</item>
/// </list>
/// </summary>
public static class ArgumentDeduplicator
{
    private static readonly HashSet<string> MultiValueGameKeys = new(StringComparer.Ordinal) { "--tweakClass" };

    public static List<string> DeduplicateJvm(IEnumerable<string> arguments)
    {
        var result = new List<string>();
        var tokens = arguments.ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var key = tokens[i];
            if (IsKeyWithSeparateValue(tokens, i))
            {
                var value = tokens[i + 1];
                i++;
                if (ContainsPair(result, key, value))
                    continue;
                result.Add(key);
                result.Add(value);
            }
            else if (!result.Contains(key))
            {
                result.Add(key);
            }
        }
        return result;
    }

    public static List<string> DeduplicateGame(IEnumerable<string> arguments)
    {
        var result = new List<string>();
        var tokens = arguments.ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var key = tokens[i];
            if (IsKeyWithSeparateValue(tokens, i))
            {
                var value = tokens[i + 1];
                i++;
                if (!MultiValueGameKeys.Contains(key))
                {
                    var existing = FindKey(result, key);
                    if (existing >= 0)
                    {
                        result[existing + 1] = value;
                        continue;
                    }
                }
                else if (ContainsPair(result, key, value))
                {
                    continue;
                }
                result.Add(key);
                result.Add(value);
            }
            else if (!result.Contains(key))
            {
                result.Add(key);
            }
        }
        return result;
    }

    // "-cp X"、"--width 854" 这种以空格分隔的键值对。
    // 值本身以 "-" 开头时，只有它是数字（例如 -50）才视为值。
    private static bool IsKeyWithSeparateValue(List<string> tokens, int index)
    {
        var key = tokens[index];
        if (!key.StartsWith('-') || key.Contains('=') || index + 1 >= tokens.Count)
            return false;
        // -Xmx4G、-XX:+UseG1GC、-Dxxx 这类参数自带值
        if (key.StartsWith("-X", StringComparison.Ordinal) || key.StartsWith("-D", StringComparison.Ordinal) || key.StartsWith("-javaagent", StringComparison.Ordinal))
            return false;
        var next = tokens[index + 1];
        return !next.StartsWith('-') || double.TryParse(next, out _);
    }

    private static int FindKey(List<string> result, string key)
    {
        for (var j = 0; j < result.Count - 1; j++)
        {
            if (result[j] == key)
                return j;
        }
        return -1;
    }

    private static bool ContainsPair(List<string> result, string key, string value)
    {
        for (var j = 0; j < result.Count - 1; j++)
        {
            if (result[j] == key && result[j + 1] == value)
                return true;
        }
        return false;
    }
}

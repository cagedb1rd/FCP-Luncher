namespace FCP.Core.Java;

/// <summary>
/// 读取 Java 安装目录下的 <c>release</c> 文件（例如 <c>JAVA_VERSION="17.0.10"</c>）。
/// 不需要启动 java 进程，速度很快；读取失败时再运行 <c>java -XshowSettings:properties -version</c>。
/// </summary>
public static class JavaReleaseFile
{
    public sealed record Info(Version Version, string? Vendor, string? Architecture);

    /// <param name="javaExecutable">java.exe 或 javaw.exe 的完整路径。</param>
    public static Info? TryRead(string javaExecutable)
    {
        var binDir = Path.GetDirectoryName(javaExecutable);
        var home = binDir is null ? null : Path.GetDirectoryName(binDir);
        if (home is null)
            return null;
        var releaseFile = Path.Combine(home, "release");
        if (!File.Exists(releaseFile))
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(releaseFile))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            values[line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"');
        }
        if (!values.TryGetValue("JAVA_VERSION", out var versionText))
            return null;
        var version = ParseJavaVersion(versionText);
        if (version is null)
            return null;
        values.TryGetValue("IMPLEMENTOR", out var vendor);
        values.TryGetValue("OS_ARCH", out var arch);
        return new Info(version, vendor, arch);
    }

    /// <summary>解析 Java 版本号："1.8.0_392" → 8.0.392；"17.0.10" → 17.0.10；"21" → 21.0；"25.0.2+10" → 25.0.2。</summary>
    public static Version? ParseJavaVersion(string text)
    {
        var cleaned = text.Trim().Split('+', '-', ' ')[0].Replace('_', '.');
        if (cleaned.StartsWith("1.", StringComparison.Ordinal))
            cleaned = cleaned[2..];
        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out var n) ? n : -1)
            .TakeWhile(n => n >= 0)
            .Take(4)
            .ToList();
        if (parts.Count == 0)
            return null;
        while (parts.Count < 2)
            parts.Add(0);
        return parts.Count switch
        {
            2 => new Version(parts[0], parts[1]),
            3 => new Version(parts[0], parts[1], parts[2]),
            _ => new Version(parts[0], parts[1], parts[2], parts[3]),
        };
    }
}

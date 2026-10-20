using System.Text;

namespace FCP.Core.Migration.Pcl;

/// <summary>
/// PCL 的 INI 文件（PCL\Setup.ini、PCL.ini、版本目录下的 PCL\Setup.ini）。
/// 格式：每行一条 Key:Value，没有 section，以第一个冒号分隔，UTF-8 编码。见 docs/02 §1。
/// 只读：迁移模块永远不写 PCL 的文件。
/// </summary>
public sealed class PclIniFile
{
    private readonly Dictionary<string, string> _values;

    private PclIniFile(Dictionary<string, string> values) => _values = values;

    public static PclIniFile Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, string> Values => _values;

    public static PclIniFile Parse(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;
            // 同一个 key 出现多次时，以最后一次为准
            values[line[..separator]] = line[(separator + 1)..];
        }
        return new PclIniFile(values);
    }

    /// <summary>读取文件；文件不存在时返回空对象。</summary>
    public static async Task<PclIniFile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return Empty;
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return Parse(content);
    }

    public bool Contains(string key) => _values.ContainsKey(key);

    public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public int? GetInt(string key) =>
        _values.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : null;

    public bool? GetBool(string key) =>
        _values.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : null;
}

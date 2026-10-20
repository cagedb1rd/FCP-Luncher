using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FCP.Core.Storage;

/// <summary>
/// 读写启动器自己的 JSON 文件：UTF-8、缩进、枚举用字符串；写入时先写临时文件再替换，避免写到一半断电导致文件损坏；
/// 读取失败（文件损坏）时把坏文件改名为 .broken 保留，并返回默认值。
/// </summary>
public static class JsonFile
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文直接写出，便于用户查看
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default) where T : class
    {
        if (!File.Exists(path))
            return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            File.Move(path, path + $".{DateTime.Now:yyyyMMddHHmmss}.broken", overwrite: true);
            return null;
        }
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
        }
        File.Move(temp, path, overwrite: true);
    }
}

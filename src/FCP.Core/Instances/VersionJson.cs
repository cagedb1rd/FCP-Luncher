using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FCP.Core.Instances;

/// <summary>
/// 版本 JSON 的定位与归一化：
/// <list type="bullet">
/// <item>优先 <c>&lt;名称&gt;.json</c>，否则取目录中任意一个含 mainClass、id 的 JSON；</item>
/// <item>HMCL 格式（含 patches 且没有 time）按 priority 合并成普通格式；</item>
/// <item>id 统一改为文件夹名（游戏中的 ${version_name}、主 jar 名都以文件夹为准）；</item>
/// <item>有 inheritsFrom 但目录中没有自己的 jar 时，jar 指向父版本（官方安装器装出的 Forge 子版本就是这样）。</item>
/// </list>
/// </summary>
public static class VersionJson
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>找到版本目录中实际使用的 JSON 文件。</summary>
    public static string? Locate(string versionDirectory)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(versionDirectory));
        var preferred = Path.Combine(versionDirectory, name + ".json");
        if (File.Exists(preferred))
            return preferred;
        if (!Directory.Exists(versionDirectory))
            return null;
        foreach (var candidate in Directory.EnumerateFiles(versionDirectory, "*.json"))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(candidate), documentOptions: DocumentOptions) is JsonObject obj &&
                    obj.ContainsKey("mainClass") && (obj.ContainsKey("id") || obj.ContainsKey("patches")))
                    return candidate;
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }
        return null;
    }

    /// <summary>读取并解析 JSON 文件为对象（不处理继承）。</summary>
    public static JsonObject Read(string path)
    {
        var text = File.ReadAllText(path);
        return JsonNode.Parse(text, documentOptions: DocumentOptions) as JsonObject
            ?? throw new InvalidDataException("版本 JSON 不是一个对象");
    }

    public static bool IsHmclFormat(JsonObject json) => json.ContainsKey("patches") && !json.ContainsKey("time");

    /// <summary>HMCL 格式：按 priority 从低到高依次合并各个 patch，得到普通格式的 JSON。</summary>
    public static JsonObject MergeHmclPatches(JsonObject json)
    {
        var patches = (json["patches"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .OrderBy(p => p["priority"]?.GetValue<int>() ?? 0)
            .ToList();
        var merged = new JsonObject();
        foreach (var patch in patches)
            MergeInto(merged, patch, prependLibraries: true);
        // patch 合并后不应再继承任何版本
        merged.Remove("inheritsFrom");
        merged.Remove("priority");
        merged.Remove("version");
        return merged;
    }

    /// <summary>
    /// 生成交给启动引擎的归一化 JSON 文本（单个文件，不展开继承；继承由引擎处理）。
    /// </summary>
    public static string Normalize(JsonObject json, string folderName, Func<string, bool> versionJarExists)
    {
        var result = IsHmclFormat(json) ? MergeHmclPatches(json) : (JsonObject)json.DeepClone();
        result["id"] = folderName;
        var inherits = result["inheritsFrom"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(inherits) && result["jar"] is null && !versionJarExists(folderName))
            result["jar"] = inherits;
        return result.ToJsonString();
    }

    /// <summary>
    /// 把 patch 合并进 target：对象逐层合并、普通字段由 patch 覆盖；
    /// libraries 中 patch 的条目放在前面（同一个类在 classpath 中先到先得）；arguments 中的数组追加。
    /// </summary>
    internal static void MergeInto(JsonObject target, JsonObject patch, bool prependLibraries)
    {
        foreach (var (key, value) in patch)
        {
            if (value is null)
                continue;
            if (key == "libraries" && value is JsonArray libraries)
            {
                var existing = target["libraries"] as JsonArray ?? [];
                var combined = new JsonArray();
                var first = prependLibraries ? libraries : existing;
                var second = prependLibraries ? existing : libraries;
                foreach (var item in first)
                    combined.Add(item?.DeepClone());
                foreach (var item in second)
                    combined.Add(item?.DeepClone());
                target["libraries"] = combined;
            }
            else if (key == "arguments" && value is JsonObject arguments)
            {
                var targetArgs = target["arguments"] as JsonObject ?? new JsonObject();
                foreach (var (kind, list) in arguments)
                {
                    var merged = targetArgs[kind] as JsonArray ?? [];
                    var copy = new JsonArray();
                    foreach (var item in merged)
                        copy.Add(item?.DeepClone());
                    if (list is JsonArray items)
                        foreach (var item in items)
                            copy.Add(item?.DeepClone());
                    targetArgs[kind] = copy;
                }
                target["arguments"] = targetArgs;
            }
            else
            {
                target[key] = value.DeepClone();
            }
        }
    }

    /// <summary>从 jar 内的 version.json 读取版本号（id 或 name），失败返回 null。</summary>
    public static string? ReadVersionFromJar(string jarPath)
    {
        try
        {
            if (!File.Exists(jarPath))
                return null;
            using var zip = ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry("version.json");
            if (entry is null)
                return null;
            using var stream = entry.Open();
            var node = JsonNode.Parse(stream) as JsonObject;
            return node?["id"]?.GetValue<string>() ?? node?["name"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

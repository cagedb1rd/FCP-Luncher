using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FCP.Core.Instances;

/// <summary>mods 文件夹中的一个 Mod 文件。禁用的 Mod 以 <c>.disabled</c> 结尾（与 PCL 等启动器相同，两边互相可见）。</summary>
public sealed record ModFile(string Path, long Size, DateTimeOffset LastWrite)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public bool IsEnabled => !Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

    /// <summary>去掉 .disabled 后的文件名。</summary>
    public string BaseName => IsEnabled ? FileName : FileName[..^".disabled".Length];
}

/// <summary>从 Mod 文件中读到的信息（读不到时各项为 null）。</summary>
public sealed record ModMetadata(string? Id, string? Name, string? Version, string? Loader, string? Description);

/// <summary>一个存档。</summary>
public sealed record SaveInfo(string Path, string Name, DateTimeOffset LastPlayed, string? IconPath);

/// <summary>资源包或光影包（文件夹或压缩包）。</summary>
public sealed record PackInfo(string Path, string Name, bool IsFolder, DateTimeOffset LastWrite);

/// <summary>读取版本游戏目录中的本地内容：Mod、存档、资源包、光影（只读，启用 / 禁用 Mod 除外）。</summary>
public static partial class InstanceContent
{
    private static readonly string[] ModExtensions = [".jar", ".zip", ".litemod"];

    public static IReadOnlyList<ModFile> ListMods(string gameDirectory)
    {
        var dir = Path.Combine(gameDirectory, "mods");
        if (!Directory.Exists(dir))
            return [];
        return new DirectoryInfo(dir).EnumerateFiles()
            .Where(f => IsModFile(f.Name))
            .Select(f => new ModFile(f.FullName, f.Length, f.LastWriteTime))
            .OrderBy(m => m.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsModFile(string name)
    {
        var baseName = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? name[..^".disabled".Length] : name;
        return ModExtensions.Any(e => baseName.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>启用或禁用 Mod（重命名为 / 去掉 .disabled），返回新的文件信息。</summary>
    public static ModFile SetEnabled(ModFile mod, bool enabled)
    {
        if (mod.IsEnabled == enabled)
            return mod;
        var target = enabled ? mod.Path[..^".disabled".Length] : mod.Path + ".disabled";
        if (File.Exists(target))
            throw new UserFacingException(enabled ? "无法启用这个 Mod" : "无法禁用这个 Mod", $"mods 文件夹中已经有同名文件：\n{Path.GetFileName(target)}\n请先处理重复的文件。");
        File.Move(mod.Path, target);
        return mod with { Path = target };
    }

    /// <summary>读取 Mod 的名称和版本：Fabric / Quilt（fabric.mod.json、quilt.mod.json）、Forge / NeoForge（mods.toml）、旧版 Forge（mcmod.info）。</summary>
    public static ModMetadata? ReadMetadata(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry("fabric.mod.json") is { } fabric)
            {
                var json = JsonNode.Parse(ReadText(fabric));
                return new ModMetadata(Str(json?["id"]), Str(json?["name"]), Str(json?["version"]), "Fabric", Str(json?["description"]));
            }
            if (zip.GetEntry("quilt.mod.json") is { } quilt)
            {
                var loader = JsonNode.Parse(ReadText(quilt))?["quilt_loader"];
                var meta = loader?["metadata"];
                return new ModMetadata(Str(loader?["id"]), Str(meta?["name"]), Str(loader?["version"]), "Quilt", Str(meta?["description"]));
            }
            var toml = zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml");
            if (toml is not null)
            {
                var text = ReadText(toml);
                var version = TomlValue(text, "version");
                if (version is null || version.Contains("${", StringComparison.Ordinal))
                    version = ManifestVersion(zip) ?? version;
                return new ModMetadata(TomlValue(text, "modId"), TomlValue(text, "displayName"), version,
                    toml.FullName.Contains("neoforge", StringComparison.Ordinal) ? "NeoForge" : "Forge", TomlValue(text, "description"));
            }
            if (zip.GetEntry("mcmod.info") is { } mcmod)
            {
                var node = JsonNode.Parse(ReadText(mcmod));
                var first = node is JsonArray array ? array.FirstOrDefault() : node?["modList"]?.AsArray().FirstOrDefault();
                return new ModMetadata(Str(first?["modid"]), Str(first?["name"]), Str(first?["version"]), "Forge", Str(first?["description"]));
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static IReadOnlyList<SaveInfo> ListSaves(string gameDirectory)
    {
        var dir = Path.Combine(gameDirectory, "saves");
        if (!Directory.Exists(dir))
            return [];
        return new DirectoryInfo(dir).EnumerateDirectories()
            .Where(d => File.Exists(Path.Combine(d.FullName, "level.dat")))
            .Select(d =>
            {
                var level = new FileInfo(Path.Combine(d.FullName, "level.dat"));
                var icon = Path.Combine(d.FullName, "icon.png");
                return new SaveInfo(d.FullName, d.Name, level.LastWriteTime, File.Exists(icon) ? icon : null);
            })
            .OrderByDescending(s => s.LastPlayed)
            .ToList();
    }

    /// <summary>资源包（resourcepacks）或光影包（shaderpacks）：文件夹或 .zip。</summary>
    public static IReadOnlyList<PackInfo> ListPacks(string gameDirectory, string folderName)
    {
        var dir = new DirectoryInfo(Path.Combine(gameDirectory, folderName));
        if (!dir.Exists)
            return [];
        var folders = dir.EnumerateDirectories().Select(d => new PackInfo(d.FullName, d.Name, true, d.LastWriteTime));
        var files = dir.EnumerateFiles("*.zip").Select(f => new PackInfo(f.FullName, Path.GetFileNameWithoutExtension(f.Name), false, f.LastWriteTime));
        return folders.Concat(files).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    /// <summary>mods.toml 中第一个 [[mods]] 段落里的值（足够读取名称和版本，不需要完整的 TOML 解析）。</summary>
    internal static string? TomlValue(string toml, string key)
    {
        var start = toml.IndexOf("[[mods]]", StringComparison.Ordinal);
        var section = start >= 0 ? toml[start..] : toml;
        var next = section.IndexOf("[[", 8, StringComparison.Ordinal);
        if (next > 0)
            section = section[..next];
        var match = Regex.Match(section, $@"^\s*{Regex.Escape(key)}\s*=\s*(?:""""""(?<v>[\s\S]*?)""""""|""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.Multiline);
        return match.Success && match.Groups["v"].Value.Trim() is { Length: > 0 } v ? v : null;
    }

    private static string? ManifestVersion(ZipArchive zip)
    {
        if (zip.GetEntry("META-INF/MANIFEST.MF") is not { } manifest)
            return null;
        var match = ImplementationVersion().Match(ReadText(manifest));
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"^Implementation-Version:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex ImplementationVersion();
}

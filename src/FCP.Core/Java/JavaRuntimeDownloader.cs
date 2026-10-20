using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace FCP.Core.Java;

/// <summary>Java 下载进度（字节）。</summary>
public readonly record struct JavaDownloadProgress(long DownloadedBytes, long TotalBytes, int DownloadedFiles, int TotalFiles);

/// <summary>Mojang 运行时清单中的一个 Java 组件。</summary>
public sealed record JavaRuntimeComponent(string Name, string VersionName, int Major, Uri ManifestUrl);

/// <summary>
/// 自动下载 Java（PCL 也有此功能）：使用 Mojang 官方启动器所用的 Java 运行时清单，
/// 逐个文件下载并校验 SHA-1，安装到数据目录下的 runtime 文件夹（D-014：不放在 C 盘）。
/// 先下载到缓存目录，全部完成后再移动到位，中途失败不会留下不完整的 Java。
/// </summary>
public sealed class JavaRuntimeDownloader(HttpClient http, AppPaths paths, ILogger<JavaRuntimeDownloader> logger)
{
    public const string AllManifestUrl = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public static string PlatformKey => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "windows-arm64",
        Architecture.X86 => "windows-x86",
        _ => "windows-x64",
    };

    /// <summary>从 all.json 中为指定主版本选择组件：优先使用指定的组件名，否则按版本号匹配主版本。</summary>
    public static JavaRuntimeComponent? ChooseComponent(JsonObject all, string platform, int major, string? preferredComponent)
    {
        if (all[platform] is not JsonObject components)
            return null;
        var candidates = new List<JavaRuntimeComponent>();
        foreach (var (name, value) in components)
        {
            if (value is not JsonArray { Count: > 0 } list || list[0] is not JsonObject entry)
                continue;
            var versionName = entry["version"]?["name"]?.GetValue<string>();
            var url = entry["manifest"]?["url"]?.GetValue<string>();
            if (versionName is null || url is null || ParseMajor(versionName) is not { } componentMajor)
                continue;
            candidates.Add(new JavaRuntimeComponent(name, versionName, componentMajor, new Uri(url)));
        }
        return candidates.FirstOrDefault(c => c.Name == preferredComponent && c.Major == major)
            ?? candidates.Where(c => c.Major == major).OrderBy(c => c.Name == "minecraft-java-exe" ? 1 : 0).FirstOrDefault();
    }

    /// <summary>清单中的版本名：“17.0.8”“21.0.7”，Java 8 是“8u202”。</summary>
    internal static int? ParseMajor(string versionName)
    {
        var text = versionName.StartsWith("1.", StringComparison.Ordinal) ? versionName[2..] : versionName;
        var digits = new string(text.TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var major) ? major : null;
    }

    /// <summary>下载并安装，返回 java.exe 路径。已经安装过的组件直接返回。</summary>
    public async Task<string> InstallAsync(int major, string? preferredComponent, IProgress<JavaDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var all = await GetJsonAsync(new Uri(AllManifestUrl), cancellationToken).ConfigureAwait(false);
        var component = ChooseComponent(all, PlatformKey, major, preferredComponent)
            ?? throw new UserFacingException("没有可下载的 Java", $"官方没有提供适用于这台电脑的 Java {major}，请手动安装 Java {major} 后在设置中添加。");

        var target = Path.Combine(paths.RuntimeDirectory, component.Name);
        var javaExe = Path.Combine(target, "bin", "java.exe");
        var marker = Path.Combine(target, ".fcp-complete");
        if (File.Exists(javaExe) && File.Exists(marker))
            return javaExe;

        logger.LogInformation("开始下载 Java：{Component} {Version}", component.Name, component.VersionName);
        var manifest = await GetJsonAsync(component.ManifestUrl, cancellationToken).ConfigureAwait(false);
        var files = new List<(string Path, Uri Url, string Sha1, long Size)>();
        var directories = new List<string>();
        foreach (var (relative, value) in manifest["files"] as JsonObject ?? [])
        {
            if (value is not JsonObject item || !IsSafeRelativePath(relative))
                continue;
            var type = item["type"]?.GetValue<string>();
            var raw = (item["downloads"] as JsonObject)?["raw"] as JsonObject;
            if (type == "directory")
                directories.Add(relative);
            else if (type == "file" && raw is not null)
                files.Add((relative, new Uri(raw["url"]!.GetValue<string>()), raw["sha1"]!.GetValue<string>(), raw["size"]!.GetValue<long>()));
        }

        // 下载到缓存目录（与数据目录同盘，最后的移动是瞬间完成的）
        var staging = Path.Combine(paths.CacheDirectory, "java-download", component.Name);
        foreach (var dir in directories)
            Directory.CreateDirectory(Path.Combine(staging, dir));
        long totalBytes = files.Sum(f => f.Size), doneBytes = 0;
        int doneFiles = 0;
        progress?.Report(new JavaDownloadProgress(0, totalBytes, 0, files.Count));
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (file, ct) =>
        {
            var path = Path.Combine(staging, file.Path);
            if (!await IsValidAsync(path, file.Sha1, file.Size, ct).ConfigureAwait(false))
                await DownloadFileAsync(file.Url, path, file.Sha1, ct).ConfigureAwait(false);
            progress?.Report(new JavaDownloadProgress(Interlocked.Add(ref doneBytes, file.Size), totalBytes, Interlocked.Increment(ref doneFiles), files.Count));
        }).ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(staging, ".fcp-complete"), component.VersionName, cancellationToken).ConfigureAwait(false);
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(paths.RuntimeDirectory);
        Directory.Move(staging, target);
        logger.LogInformation("Java 下载完成：{Path}", javaExe);
        return javaExe;
    }

    private async Task<JsonObject> GetJsonAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            return (await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false))?.AsObject()
                ?? throw new InvalidDataException("空的 JSON");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            throw new UserFacingException("下载 Java 失败", "无法获取 Java 下载列表，请检查网络连接后重试。", ex);
        }
    }

    private async Task DownloadFileAsync(Uri url, string path, string sha1, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".download";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using (var source = await http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
                await using (var target = File.Create(temp))
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                if (!await IsValidAsync(temp, sha1, null, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException($"文件校验失败：{url}");
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is HttpRequestException or IOException or InvalidDataException)
            {
                logger.LogInformation("下载失败，第 {Attempt} 次重试：{Url}（{Reason}）", attempt, url, ex.Message);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                throw new UserFacingException("下载 Java 失败", "Java 文件下载失败，请检查网络连接后重试。已经下载的部分会保留，重试时不需要从头开始。", ex);
            }
        }
    }

    private static async Task<bool> IsValidAsync(string path, string sha1, long? size, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || (size is { } expected && info.Length != expected))
            return false;
        await using var stream = info.OpenRead();
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash) == sha1.ToLowerInvariant();
    }

    /// <summary>清单中的路径不能跳出安装目录。</summary>
    internal static bool IsSafeRelativePath(string relative) =>
        relative.Length > 0 && !Path.IsPathRooted(relative) && !relative.Split('/', '\\').Contains("..");
}

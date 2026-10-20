using System.Text;
using CmlLib.Core.VersionLoader;
using CmlLib.Core.VersionMetadata;
using FCP.Core.Instances;

namespace FCP.Engine.CmlLib;

/// <summary>
/// 本地版本加载器：替代 CmlLib 的 LocalJsonVersionLoader，在交给 CmlLib 解析之前先用 <see cref="VersionJson"/> 归一化：
/// 支持文件名与 id 不一致、HMCL 的 patches 格式、Forge 子版本目录中没有自己的 jar 等情况。
/// </summary>
internal sealed class FcpVersionLoader(string versionsDirectory) : IVersionLoader
{
    public ValueTask<VersionMetadataCollection> GetVersionMetadatasAsync(CancellationToken cancellationToken = default)
    {
        var metadata = new List<IVersionMetadata>();
        if (Directory.Exists(versionsDirectory))
        {
            foreach (var dir in Directory.EnumerateDirectories(versionsDirectory))
            {
                var name = Path.GetFileName(dir);
                if (VersionJson.Locate(dir) is { } jsonPath)
                    metadata.Add(new NormalizedVersionMetadata(name, jsonPath, versionsDirectory));
            }
        }
        return new ValueTask<VersionMetadataCollection>(new VersionMetadataCollection(metadata, null, null));
    }

    private sealed class NormalizedVersionMetadata(string name, string jsonPath, string versionsDirectory)
        : JsonVersionMetadata(new JsonVersionMetadataModel { Id = name, Type = "local" })
    {
        protected override ValueTask<Stream> GetVersionJsonStream(CancellationToken cancellationToken)
        {
            var json = VersionJson.Read(jsonPath);
            var normalized = VersionJson.Normalize(json, name, id => File.Exists(Path.Combine(versionsDirectory, id, id + ".jar")));
            return new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(normalized)));
        }
    }
}

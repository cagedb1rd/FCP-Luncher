using System.Text;

namespace FCP.Core.Tests;

/// <summary>在临时目录中搭建合成的 .minecraft，测试结束后删除。</summary>
public sealed class TempMinecraft : IDisposable
{
    public TempMinecraft()
    {
        Root = Path.Combine(Path.GetTempPath(), "fcp-tests", Guid.NewGuid().ToString("N"), ".minecraft");
        Directory.CreateDirectory(Path.Combine(Root, "versions"));
    }

    public string Root { get; }

    public string VersionDir(string name) => Path.Combine(Root, "versions", name);

    /// <summary>写入 versions\name\fileName（默认 name.json）。</summary>
    public TempMinecraft Version(string name, string json, string? fileName = null, bool withJar = true)
    {
        var dir = VersionDir(name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName ?? name + ".json"), json, new UTF8Encoding(false));
        if (withJar)
            File.WriteAllBytes(Path.Combine(dir, name + ".jar"), []);
        return this;
    }

    public TempMinecraft File_(string relativePath, string content = "")
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return this;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>最小的原版 JSON。</summary>
    public static string Vanilla(string id, string type = "release", int java = 17, string releaseTime = "2023-06-12T13:25:51+00:00") => $$"""
        {
          "id": "{{id}}", "type": "{{type}}", "mainClass": "net.minecraft.client.main.Main",
          "releaseTime": "{{releaseTime}}", "javaVersion": { "component": "java-runtime-gamma", "majorVersion": {{java}} },
          "arguments": { "game": ["--username", "${auth_player_name}"], "jvm": ["-cp", "${classpath}"] },
          "libraries": [ { "name": "com.mojang:brigadier:1.0.18" } ]
        }
        """;
}

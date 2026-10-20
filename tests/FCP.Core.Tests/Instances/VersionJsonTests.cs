using System.Text.Json.Nodes;
using FCP.Core.Instances;

namespace FCP.Core.Tests.Instances;

public class VersionJsonTests
{
    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Normalize_UsesFolderNameAsId()
    {
        var result = Parse(VersionJson.Normalize(Parse("""{"id":"1.20.1","mainClass":"a.B"}"""), "我的整合包", _ => true));
        result["id"]!.GetValue<string>().ShouldBe("我的整合包");
        result["jar"].ShouldBeNull();
    }

    [Fact]
    public void Normalize_ChildWithoutOwnJar_UsesParentJar()
    {
        // Forge/Fabric 安装后的子版本通常没有自己的 jar，需要指向父版本的 jar
        var json = Parse("""{"id":"forge","inheritsFrom":"1.20.1","mainClass":"a.B"}""");
        Parse(VersionJson.Normalize(json, "Forge 1.20.1", _ => false))["jar"]!.GetValue<string>().ShouldBe("1.20.1");
        Parse(VersionJson.Normalize(json, "Forge 1.20.1", _ => true))["jar"].ShouldBeNull();
    }

    [Fact]
    public void Normalize_KeepsExplicitJar()
    {
        var json = Parse("""{"id":"x","inheritsFrom":"1.20.1","jar":"custom"}""");
        Parse(VersionJson.Normalize(json, "x", _ => false))["jar"]!.GetValue<string>().ShouldBe("custom");
    }

    [Fact]
    public void Normalize_MergesHmclPatchesByPriority()
    {
        var json = Parse("""
        {
          "id": "hmcl-pack",
          "patches": [
            { "id": "forge", "priority": 30000, "mainClass": "cpw.Forge",
              "libraries": [ { "name": "net.minecraftforge:forge:47" } ],
              "arguments": { "game": [ "--launchTarget", "forgeclient" ] } },
            { "id": "game", "priority": 0, "mainClass": "net.minecraft.client.main.Main", "assets": "5",
              "libraries": [ { "name": "com.mojang:brigadier:1" } ],
              "arguments": { "game": [ "--username", "${auth_player_name}" ] } }
          ]
        }
        """);
        VersionJson.IsHmclFormat(json).ShouldBeTrue();

        var result = Parse(VersionJson.Normalize(json, "hmcl-pack", _ => true));

        result["id"]!.GetValue<string>().ShouldBe("hmcl-pack");
        result["mainClass"]!.GetValue<string>().ShouldBe("cpw.Forge");
        result["assets"]!.GetValue<string>().ShouldBe("5");
        result["patches"].ShouldBeNull();
        // 优先级高的 patch 的库排在前面
        result["libraries"]!.AsArray().Select(l => l!["name"]!.GetValue<string>())
            .ShouldBe(["net.minecraftforge:forge:47", "com.mojang:brigadier:1"]);
        // 参数按优先级从低到高追加
        result["arguments"]!["game"]!.AsArray().Select(a => a!.GetValue<string>())
            .ShouldBe(["--username", "${auth_player_name}", "--launchTarget", "forgeclient"]);
    }
}

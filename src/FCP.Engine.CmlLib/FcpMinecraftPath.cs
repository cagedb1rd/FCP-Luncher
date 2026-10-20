using CmlLib.Core;

namespace FCP.Engine.CmlLib;

/// <summary>
/// 适配 CmlLib 的路径模型：
/// <list type="bullet">
/// <item>libraries / versions / assets 始终位于 .minecraft 根目录；</item>
/// <item>游戏目录（BasePath → --gameDir）可以是版本目录（版本隔离）；</item>
/// <item>natives 解压到 <c>versions\&lt;id&gt;\natives-fcp</c>。CmlLib 会在解压前清空 natives 目录，
/// 使用自己的目录可以避免删掉其他启动器的文件（见 docs/04 §6.2）。</item>
/// </list>
/// </summary>
internal sealed class FcpMinecraftPath : MinecraftPath
{
    public const string NativesDirectoryName = "natives-fcp";

    public FcpMinecraftPath(string rootDirectory, string gameDirectory) : base(rootDirectory)
    {
        RootDirectory = BasePath;
        BasePath = NormalizePath(gameDirectory);
        // 运行时目录不跟随游戏目录，固定在根目录（只作为 Java 的发现来源，不写入）
        Runtime = NormalizePath(Path.Combine(RootDirectory, "runtime"));
    }

    public string RootDirectory { get; }

    public override string GetNativePath(string id) => NormalizePath(Path.Combine(Versions, id, NativesDirectoryName));
}

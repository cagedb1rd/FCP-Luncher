using System.Text;
using FCP.Core.Launch;

namespace FCP.Core.Tests.Launch;

public class GameOutputDecoderTests
{
    private static readonly Encoding Gbk = GetGbk();

    private static Encoding GetGbk()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    [Fact]
    public async Task MixedUtf8AndGbkLines_AreBothReadable()
    {
        // 同一个输出流中：System.out 写 UTF-8，log4j 按系统编码（GBK）写
        var bytes = new MemoryStream();
        bytes.Write(Encoding.UTF8.GetBytes("[main/INFO]: 正在加载 模组\r\n"));
        bytes.Write(Gbk.GetBytes("com.sun.jna.platform.win32.Win32Exception: 参数错误。\r\n"));
        bytes.Write(Encoding.UTF8.GetBytes("plain ascii\n"));
        bytes.Write(Encoding.UTF8.GetBytes("最后一行没有换行"));
        bytes.Position = 0;

        var lines = new List<string>();
        await GameOutputDecoder.ReadLinesAsync(bytes, lines.Add, TestContext.Current.CancellationToken);

        lines.ShouldBe([
            "[main/INFO]: 正在加载 模组",
            GameOutputDecoder.SystemAnsi.CodePage == 936 ? "com.sun.jna.platform.win32.Win32Exception: 参数错误。" : lines[1],
            "plain ascii",
            "最后一行没有换行",
        ]);
        lines[1].ShouldNotContain("�");
    }

    [Fact]
    public async Task LinesSplitAcrossReads_AreJoined()
    {
        // 一个中文字符的 UTF-8 字节被拆到两次读取中
        var text = new string('啊', 10_000) + "\n下一行\n";
        var lines = new List<string>();
        await GameOutputDecoder.ReadLinesAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), lines.Add, TestContext.Current.CancellationToken);
        lines.ShouldBe([new string('啊', 10_000), "下一行"]);
    }
}

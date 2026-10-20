using System.Text;

namespace FCP.Core.Launch;

/// <summary>
/// 游戏输出的逐行解码。游戏的输出经常混用两种编码：<c>System.out</c> 按 UTF-8 输出（我们传了 stdout.encoding），
/// 而 log4j 等按 Java 默认字符集输出（<c>file.encoding=COMPAT</c> 或 Java 17 及以下时是系统编码，中文系统为 GBK）。
/// 因此每一行先按严格的 UTF-8 解码，不是合法 UTF-8 时再按系统编码解码，中文日志就不会乱码。
/// </summary>
public static class GameOutputDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static GameOutputDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>系统的 ANSI 编码（中文 Windows 为 GBK / 936），也就是 Java 的 native.encoding。</summary>
    public static Encoding SystemAnsi { get; } = GetSystemAnsi();

    private static Encoding GetSystemAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            // 注册 CodePagesEncodingProvider 后，代码页 0 表示操作系统当前的 ANSI 代码页
            return Encoding.GetEncoding(0);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }

    public static string Decode(ReadOnlySpan<byte> line)
    {
        try
        {
            return StrictUtf8.GetString(line);
        }
        catch (DecoderFallbackException)
        {
            return SystemAnsi.GetString(line);
        }
    }

    /// <summary>从流中逐行读取并解码（去掉行尾的 \r\n），直到流结束。</summary>
    public static async Task ReadLinesAsync(Stream stream, Action<string> onLine, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[16 * 1024];
        var pending = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var start = 0;
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != (byte)'\n')
                    continue;
                pending.Write(buffer, start, i - start);
                EmitLine(pending, onLine);
                start = i + 1;
            }
            pending.Write(buffer, start, read - start);
        }
        if (pending.Length > 0)
            EmitLine(pending, onLine);
    }

    private static void EmitLine(MemoryStream pending, Action<string> onLine)
    {
        var bytes = pending.GetBuffer().AsSpan(0, (int)pending.Length);
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            bytes = bytes[..^1];
        onLine(Decode(bytes));
        pending.SetLength(0);
    }
}

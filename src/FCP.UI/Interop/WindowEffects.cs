using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FCP.UI.Interop;

/// <summary>窗口外观相关的 Win32 / DWM 调用。</summary>
public static partial class WindowEffects
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    /// <summary>在 Windows 11 上为无边框窗口启用系统圆角和阴影；在 Windows 10 上调用无效果，也不会报错。</summary>
    public static void TryEnableRoundedCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var preference = DwmwcpRound;
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

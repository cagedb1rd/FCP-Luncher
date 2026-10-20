using System.Windows.Media;

namespace FCP.UI.Controls;

/// <summary>像素画的通用工具：确定性的伪随机数与颜色运算。所有像素画都是代码生成的原创图案。</summary>
internal static class PixelArt
{
    /// <summary>由字符串得到稳定的种子（与运行环境无关）。</summary>
    public static uint Seed(string? text)
    {
        uint hash = 2166136261;
        foreach (var c in text ?? "")
            hash = (hash ^ c) * 16777619;
        return hash == 0 ? 1 : hash;
    }

    /// <summary>xorshift32：给定种子与坐标，返回 [0, 1) 的确定性随机数。</summary>
    public static double Noise(uint seed, int x, int y)
    {
        var v = seed ^ (uint)(x * 374761393) ^ (uint)(y * 668265263);
        v ^= v << 13;
        v ^= v >> 17;
        v ^= v << 5;
        return (v & 0xFFFFFF) / (double)0x1000000;
    }

    public static Color Shade(Color color, double factor) => Color.FromArgb(
        color.A,
        (byte)Math.Clamp(color.R * factor, 0, 255),
        (byte)Math.Clamp(color.G * factor, 0, 255),
        (byte)Math.Clamp(color.B * factor, 0, 255));

    public static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t),
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    public static SolidColorBrush Brush(Color color)
    {
        lock (BrushCache)
        {
            if (!BrushCache.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                BrushCache[color] = brush;
            }
            return brush;
        }
    }
}

using System.Windows;
using System.Windows.Media;

namespace FCP.UI.Controls;

/// <summary>
/// 程序生成的像素风景（原创）：分层的天空、太阳、云、远山、近处的草地与泥土、树和小花。
/// 同一个 <see cref="Seed"/> 总是得到同一幅画；宽度变化时自动延伸。
/// </summary>
public sealed class PixelLandscape : FrameworkElement
{
    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
        nameof(PixelSize), typeof(double), typeof(PixelLandscape), new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeedProperty = DependencyProperty.Register(
        nameof(Seed), typeof(int), typeof(PixelLandscape), new FrameworkPropertyMetadata(7, FrameworkPropertyMetadataOptions.AffectsRender));

    public double PixelSize
    {
        get => (double)GetValue(PixelSizeProperty);
        set => SetValue(PixelSizeProperty, value);
    }

    public int Seed
    {
        get => (int)GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(PixelLandscape), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>圆角裁剪半径，用于放进圆角卡片中。</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public PixelLandscape()
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = PixelSize;
        var w = (int)Math.Ceiling(ActualWidth / p);
        var h = (int)Math.Ceiling(ActualHeight / p);
        if (w <= 0 || h <= 0)
            return;
        var seed = (uint)Seed * 2246822519u + 3;
        var canvas = new Color[w, h];

        // 天空：分成几条色带，带有像素感
        Color skyTop = PixelArt.Hex("#7CC6F2"), skyBottom = PixelArt.Hex("#D8F1FF");
        for (var y = 0; y < h; y++)
        {
            var band = Math.Floor(y / (double)h * 7) / 6.0;
            var color = PixelArt.Lerp(skyTop, skyBottom, Math.Min(1, band));
            for (var x = 0; x < w; x++)
                canvas[x, y] = color;
        }

        // 太阳（带一圈光晕）
        var sunX = (int)(w * 0.8);
        var sunY = Math.Max(3, (int)(h * 0.22));
        for (var y = -5; y <= 5; y++)
        {
            for (var x = -5; x <= 5; x++)
            {
                var d = Math.Sqrt(x * x + y * y);
                Set(canvas, sunX + x, sunY + y, d <= 2.6 ? PixelArt.Hex("#FFE27A") : d <= 3.8 ? PixelArt.Hex("#FFF0B0") : null);
            }
        }

        // 云
        for (var i = 0; i < 3; i++)
        {
            var cx = (int)(PixelArt.Noise(seed, i, 1) * w * 0.7) + 2;
            var cy = 2 + (int)(PixelArt.Noise(seed, i, 2) * Math.Max(1, h * 0.25));
            var len = 6 + (int)(PixelArt.Noise(seed, i, 3) * 8);
            for (var x = 0; x < len; x++)
            {
                Set(canvas, cx + x, cy + 1, PixelArt.Hex("#FFFFFF"));
                Set(canvas, cx + x, cy + 2, PixelArt.Hex("#EAF6FF"));
                if (x > 1 && x < len - 2)
                    Set(canvas, cx + x, cy, PixelArt.Hex("#FFFFFF"));
                if (x > 2 && x < len / 2 + 2)
                    Set(canvas, cx + x, cy - 1, PixelArt.Hex("#FFFFFF"));
            }
        }

        // 远山
        double Wave(double x, double a, double f, uint s) =>
            a * (Math.Sin(x * f + PixelArt.Noise(s, 1, 1) * 6.28) + 0.5 * Math.Sin(x * f * 2.3 + PixelArt.Noise(s, 2, 2) * 6.28));
        var farBase = h * 0.58;
        for (var x = 0; x < w; x++)
        {
            var top = (int)(farBase + Wave(x, h * 0.09, 0.09, seed + 5));
            for (var y = Math.Max(0, top); y < h; y++)
                canvas[x, y] = y == top ? PixelArt.Hex("#8FD19A") : PixelArt.Hex("#78C08A");
        }

        // 近处的草地 + 泥土 + 石头
        var nearBase = h * 0.74;
        var grassTop = new int[w];
        for (var x = 0; x < w; x++)
        {
            var top = (int)(nearBase + Wave(x, h * 0.06, 0.13, seed + 9));
            grassTop[x] = top;
            for (var y = Math.Max(0, top); y < h; y++)
            {
                var depth = y - top;
                var n = PixelArt.Noise(seed + 17, x, y);
                canvas[x, y] = depth switch
                {
                    0 => n < 0.5 ? PixelArt.Hex("#6CCB52") : PixelArt.Hex("#62C04E"),
                    1 => n < 0.35 ? PixelArt.Hex("#62C04E") : PixelArt.Hex("#4FA83F"),
                    _ when n < 0.08 => PixelArt.Hex("#9A9DA0"),
                    _ => n < 0.5 ? PixelArt.Hex("#94643A") : PixelArt.Hex("#87592F"),
                };
            }
        }

        // 树（树干 + 树冠）
        var treeCount = Math.Max(2, w / 28);
        for (var i = 0; i < treeCount; i++)
        {
            var tx = (int)((i + 0.3 + PixelArt.Noise(seed, i, 7) * 0.4) * w / treeCount);
            if (tx < 2 || tx >= w - 3 || Math.Abs(tx - sunX) < 4)
                continue;
            var ground = grassTop[tx];
            for (var y = 1; y <= 3; y++)
                Set(canvas, tx, ground - y, PixelArt.Hex("#6D4A2C"));
            for (var y = 4; y <= 7; y++)
            {
                var half = y is 4 or 7 ? 1 : 2;
                for (var x = -half; x <= half; x++)
                {
                    var leaf = PixelArt.Noise(seed + 23, tx + x, y) < 0.3 ? PixelArt.Hex("#3E9E4B") : PixelArt.Hex("#2F8A3E");
                    Set(canvas, tx + x, ground - y, leaf);
                }
            }
        }

        // 小花
        for (var x = 1; x < w - 1; x++)
        {
            var n = PixelArt.Noise(seed + 41, x, 0);
            if (n < 0.06)
                Set(canvas, x, grassTop[x] - 1, n < 0.03 ? PixelArt.Hex("#FF6B6B") : PixelArt.Hex("#FFD93D"));
        }

        // 按行合并相同颜色的像素后绘制
        if (CornerRadius > 0)
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius, CornerRadius));
        for (var y = 0; y < h; y++)
        {
            var start = 0;
            for (var x = 1; x <= w; x++)
            {
                if (x < w && canvas[x, y] == canvas[start, y])
                    continue;
                dc.DrawRectangle(PixelArt.Brush(canvas[start, y]), null, new Rect(start * p, y * p, (x - start) * p, p));
                start = x;
            }
        }
        if (CornerRadius > 0)
            dc.Pop();
    }

    private static void Set(Color[,] canvas, int x, int y, Color? color)
    {
        if (color is { } c && x >= 0 && y >= 0 && x < canvas.GetLength(0) && y < canvas.GetLength(1))
            canvas[x, y] = c;
    }
}

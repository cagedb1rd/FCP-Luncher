using System.Windows;
using System.Windows.Media;

namespace FCP.UI.Controls;

/// <summary>版本类型，决定方块图标的配色。</summary>
public enum BlockKind
{
    Vanilla,
    Forge,
    NeoForge,
    Fabric,
    Quilt,
    OptiFine,
    Snapshot,
    Old,
    AprilFools,
    Error,
}

/// <summary>
/// 等距视角的像素方块图标（原创图案）：每个面切成 N×N 个小格并加入轻微的明暗变化，
/// 顶面最亮、左面次之、右面最暗。不同的版本类型使用不同的配色。
/// </summary>
public sealed class BlockIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(BlockKind), typeof(BlockIcon), new FrameworkPropertyMetadata(BlockKind.Vanilla, FrameworkPropertyMetadataOptions.AffectsRender));

    public BlockKind Kind
    {
        get => (BlockKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private const int Cells = 5;

    private sealed record Palette(Color Top, Color Side, Color? SideTopRow = null, Color? Detail = null, double DetailChance = 0);

    private static Palette PaletteOf(BlockKind kind) => kind switch
    {
        BlockKind.Vanilla => new(PixelArt.Hex("#62C04E"), PixelArt.Hex("#94643A"), PixelArt.Hex("#5AB247"), PixelArt.Hex("#7E5330"), 0.25),
        BlockKind.Forge => new(PixelArt.Hex("#7D8A99"), PixelArt.Hex("#566270"), null, PixelArt.Hex("#E9853A"), 0.12),
        BlockKind.NeoForge => new(PixelArt.Hex("#F0923F"), PixelArt.Hex("#D2702A"), null, PixelArt.Hex("#FFC27A"), 0.18),
        BlockKind.Fabric => new(PixelArt.Hex("#E4D8B8"), PixelArt.Hex("#C8B78E"), null, PixelArt.Hex("#A8966D"), 0.2),
        BlockKind.Quilt => new(PixelArt.Hex("#A57BE0"), PixelArt.Hex("#7F57C2"), null, PixelArt.Hex("#C9A8F5"), 0.3),
        BlockKind.OptiFine => new(PixelArt.Hex("#47B3D6"), PixelArt.Hex("#2E8DB0"), null, PixelArt.Hex("#9BE0F2"), 0.2),
        BlockKind.Snapshot => new(PixelArt.Hex("#E3B048"), PixelArt.Hex("#B98431"), null, PixelArt.Hex("#5D4A2A"), 0.18),
        BlockKind.Old => new(PixelArt.Hex("#A3A6A8"), PixelArt.Hex("#808487"), null, PixelArt.Hex("#5F6366"), 0.35),
        BlockKind.AprilFools => new(PixelArt.Hex("#F7D54A"), PixelArt.Hex("#DDB22C"), null, PixelArt.Hex("#FFF2A6"), 0.25),
        _ => new(PixelArt.Hex("#E25B55"), PixelArt.Hex("#B8403B"), null, PixelArt.Hex("#7A2522"), 0.25),
    };

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 32 : Math.Min(availableSize.Width, 32),
        double.IsInfinity(availableSize.Height) ? 32 : Math.Min(availableSize.Height, 32));

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
            return;
        var ox = (ActualWidth - size) / 2;
        var oy = (ActualHeight - size) / 2;
        var p = PaletteOf(Kind);
        var seed = (uint)Kind * 2654435761u + 7;

        // 等距方块的 7 个关键点
        Point P(double x, double y) => new(ox + x * size, oy + y * size);
        var top = P(0.5, 0.02);
        var left = P(0.04, 0.27);
        var right = P(0.96, 0.27);
        var center = P(0.5, 0.52);
        var leftBottom = P(0.04, 0.75);
        var rightBottom = P(0.96, 0.75);
        var bottom = P(0.5, 0.98);

        // 顶面：left → top → right → center
        DrawFace(dc, left, top, right, center, (i, j) => Cell(p.Top, p, seed, 0, i, j, 1.0));
        // 左面：left → center → bottom → leftBottom（第 0 行靠近顶面）
        DrawFace(dc, left, center, bottom, leftBottom, (i, j) =>
            Cell(j == 0 && p.SideTopRow is { } row ? row : p.Side, p, seed, 1, i, j, 0.9));
        // 右面：center → right → rightBottom → bottom
        DrawFace(dc, center, right, rightBottom, bottom, (i, j) =>
            Cell(j == 0 && p.SideTopRow is { } row ? row : p.Side, p, seed, 2, i, j, 0.74));

        // 顶部高光边
        var highlight = new Pen(PixelArt.Brush(Color.FromArgb(90, 255, 255, 255)), Math.Max(1, size / 32));
        highlight.Freeze();
        dc.DrawLine(highlight, left, top);
        dc.DrawLine(highlight, top, right);
    }

    private static Color Cell(Color baseColor, Palette p, uint seed, int face, int i, int j, double light)
    {
        var n = PixelArt.Noise(seed + (uint)face * 977, i, j);
        var color = p.Detail is { } detail && n < p.DetailChance && !(face > 0 && j == 0 && p.SideTopRow is not null)
            ? detail
            : baseColor;
        return PixelArt.Shade(color, light * (0.93 + 0.12 * PixelArt.Noise(seed + 31, i * 7 + face, j * 3)));
    }

    // 把四边形 (a→b 为第一条边，d→c 为对边) 切成 Cells×Cells 个小格，逐格填色
    private static void DrawFace(DrawingContext dc, Point a, Point b, Point c, Point d, Func<int, int, Color> colorAt)
    {
        Point Bilinear(double u, double v)
        {
            var top = new Point(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
            var bottom = new Point(d.X + (c.X - d.X) * u, d.Y + (c.Y - d.Y) * u);
            return new Point(top.X + (bottom.X - top.X) * v, top.Y + (bottom.Y - top.Y) * v);
        }

        for (var j = 0; j < Cells; j++)
        {
            for (var i = 0; i < Cells; i++)
            {
                double u0 = (double)i / Cells, u1 = (double)(i + 1) / Cells;
                double v0 = (double)j / Cells, v1 = (double)(j + 1) / Cells;
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(Bilinear(u0, v0), true, true);
                    ctx.LineTo(Bilinear(u1, v0), false, false);
                    ctx.LineTo(Bilinear(u1, v1), false, false);
                    ctx.LineTo(Bilinear(u0, v1), false, false);
                }
                geometry.Freeze();
                var brush = PixelArt.Brush(colorAt(i, j));
                // 用同色描边消除相邻小格之间的细缝
                var seam = new Pen(brush, 0.6);
                seam.Freeze();
                dc.DrawGeometry(brush, seam, geometry);
            }
        }
    }
}

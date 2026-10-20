using System.Windows;
using System.Windows.Media;

namespace FCP.UI.Controls;

/// <summary>
/// 根据玩家名生成的 8×8 像素头像（原创的程序化图案，用于还没有皮肤数据时）。
/// 同一个名字总是得到同一张脸；有皮肤文件后改为渲染真实的皮肤头部。
/// </summary>
public sealed class PixelAvatar : FrameworkElement
{
    public static readonly DependencyProperty PlayerNameProperty = DependencyProperty.Register(
        nameof(PlayerName), typeof(string), typeof(PixelAvatar), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(PixelAvatar), new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public string PlayerName
    {
        get => (string)GetValue(PlayerNameProperty);
        set => SetValue(PlayerNameProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private static readonly string[] Skins = ["#F2C9A0", "#E8B48A", "#D19A6E", "#B57B52", "#8D5A3B", "#F6D7BE"];
    private static readonly string[] Hairs = ["#3B2A1E", "#5A3A22", "#8A5A2B", "#C98A3B", "#2B2B33", "#B8442E", "#E8D27A", "#4B6CB7", "#6E4BAE"];
    private static readonly string[] Eyes = ["#3C6FD8", "#2F8F5B", "#6B4A2E", "#4A4A55", "#8A3FB8"];

    public PixelAvatar()
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
            return;
        var seed = PixelArt.Seed(PlayerName);
        int Pick(int salt, int count) => (int)(PixelArt.Noise(seed, salt, 11) * count);

        var skin = PixelArt.Hex(Skins[Pick(1, Skins.Length)]);
        var hairIndex = Pick(2, Hairs.Length);
        var hair = PixelArt.Hex(Hairs[hairIndex]);
        var eye = PixelArt.Hex(Eyes[Pick(3, Eyes.Length)]);
        var hairStyle = Pick(4, 4);   // 刘海形状
        // 胡子只配自然发色（前 7 种），彩色头发配胡子看起来像衣领
        var hasBeard = hairIndex < 7 && Pick(5, 6) == 0;
        var mouth = PixelArt.Shade(skin, 0.68);

        var grid = new Color[8, 8];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                grid[x, y] = skin;

        // 头发：前两行 + 两侧 + 不同形状的刘海
        for (var x = 0; x < 8; x++)
        {
            grid[x, 0] = hair;
            grid[x, 1] = hair;
        }
        for (var y = 2; y < 4; y++)
        {
            grid[0, y] = hair;
            grid[7, y] = hair;
        }
        switch (hairStyle)
        {
            case 0: grid[1, 2] = hair; grid[2, 2] = hair; grid[6, 2] = hair; break;
            case 1: for (var x = 1; x < 5; x++) grid[x, 2] = hair; break;
            case 2: grid[3, 2] = hair; grid[4, 2] = hair; break;
            default: grid[1, 2] = hair; grid[6, 2] = hair; break;
        }

        // 眼睛（白 + 瞳孔）、鼻子、嘴
        var white = PixelArt.Hex("#FFFFFF");
        grid[1, 4] = white; grid[2, 4] = eye;
        grid[5, 4] = eye; grid[6, 4] = white;
        grid[3, 5] = PixelArt.Shade(skin, 0.85); grid[4, 5] = PixelArt.Shade(skin, 0.85);
        for (var x = 2; x < 6; x++)
            grid[x, 6] = mouth;
        if (hasBeard)
        {
            var beard = PixelArt.Shade(hair, 1.05);
            for (var x = 1; x < 7; x++) grid[x, 7] = beard;
            grid[1, 6] = beard; grid[6, 6] = beard;
        }

        var cell = size / 8;
        var ox = (ActualWidth - size) / 2;
        var oy = (ActualHeight - size) / 2;
        dc.PushClip(new RectangleGeometry(new Rect(ox, oy, size, size), CornerRadius, CornerRadius));
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                // 轻微的明暗变化让像素更有质感
                var color = PixelArt.Shade(grid[x, y], 0.95 + 0.08 * PixelArt.Noise(seed, x, y + 20));
                dc.DrawRectangle(PixelArt.Brush(color), null, new Rect(ox + x * cell, oy + y * cell, Math.Ceiling(cell), Math.Ceiling(cell)));
            }
        }
        dc.Pop();
    }
}

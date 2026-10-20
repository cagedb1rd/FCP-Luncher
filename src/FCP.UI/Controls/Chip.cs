using System.Windows;
using System.Windows.Controls;

namespace FCP.UI.Controls;

public enum ChipTone
{
    Brand,
    Amber,
    Sky,
    Purple,
    Rose,
    Gray,
}

/// <summary>小型彩色标签，例如“1.20.1”“Forge 47.4.10”“LittleSkin”。样式见 Themes/Controls.xaml。</summary>
public sealed class Chip : ContentControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(ChipTone), typeof(Chip), new PropertyMetadata(ChipTone.Brand));

    public ChipTone Tone
    {
        get => (ChipTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}

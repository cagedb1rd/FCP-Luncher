using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FCP.UI.Controls;

/// <summary>
/// 内容切换时播放“淡入 + 轻微位移”动画的 ContentControl，用于页面切换。
/// 动画时长 220 ms，缓出；可通过 <see cref="Offset"/> 控制位移方向和距离。
/// </summary>
public class TransitionContentControl : ContentControl
{
    public static readonly DependencyProperty OffsetProperty =
        DependencyProperty.Register(nameof(Offset), typeof(double), typeof(TransitionContentControl), new PropertyMetadata(18.0));

    public static readonly DependencyProperty IsAnimationEnabledProperty =
        DependencyProperty.Register(nameof(IsAnimationEnabled), typeof(bool), typeof(TransitionContentControl), new PropertyMetadata(true));

    /// <summary>新内容进入时的初始水平位移（像素），负数表示从左侧进入。</summary>
    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (!IsAnimationEnabled || !SystemParameters.ClientAreaAnimation || oldContent is null)
            return;

        var translate = new TranslateTransform(Offset, 0);
        RenderTransform = translate;
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Offset, 0, duration) { EasingFunction = ease });
    }
}

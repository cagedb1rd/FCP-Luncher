using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FCP.UI.Controls;

/// <summary>
/// 动效附加属性。
/// <c>Motion.Entrance="True"</c>：元素加载时淡入并向上滑入；配合 <c>Motion.Delay</c>（毫秒）实现依次出现。
/// 系统关闭了“动画效果”时直接显示。
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty EntranceProperty = DependencyProperty.RegisterAttached(
        "Entrance", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEntranceChanged));

    public static readonly DependencyProperty DelayProperty = DependencyProperty.RegisterAttached(
        "Delay", typeof(int), typeof(Motion), new PropertyMetadata(0));

    public static bool GetEntrance(DependencyObject d) => (bool)d.GetValue(EntranceProperty);
    public static void SetEntrance(DependencyObject d, bool value) => d.SetValue(EntranceProperty, value);
    public static int GetDelay(DependencyObject d) => (int)d.GetValue(DelayProperty);
    public static void SetDelay(DependencyObject d, int value) => d.SetValue(DelayProperty, value);

    private static void OnEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
            return;
        if (!SystemParameters.ClientAreaAnimation)
            return;
        element.Opacity = 0;
        element.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;
        var delay = TimeSpan.FromMilliseconds(GetDelay(element));
        var duration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = ease });

        // 只在元素没有使用其他变换时做位移：按钮样式里的 ScaleTransform（按下缩放）不能被替换，否则样式动画会找不到目标
        if (element.RenderTransform is not (null or TranslateTransform) && element.RenderTransform != Transform.Identity)
            return;
        var translate = new TranslateTransform(0, 14);
        element.RenderTransform = translate;
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { BeginTime = delay, EasingFunction = ease });
    }
}

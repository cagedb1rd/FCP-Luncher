using System.Windows;

namespace FCP.App.ViewModels;

/// <summary>把后台线程的回调切回界面线程（游戏会话、Java 扫描等事件在后台线程触发）。</summary>
internal static class Ui
{
    public static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.InvokeAsync(action);
    }
}

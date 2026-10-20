using System.Diagnostics;
using FCP.Core.Launch;
using FCP.Core.Platform;

namespace FCP.Core.Tests.Launch;

public class GameSessionTests
{
    private sealed class NoWindows : IWindowLocator
    {
        public bool HasVisibleWindow(int processId) => false;
    }

    private static Process StartExiting(int code)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe", $"/c echo hello & exit {code}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.Start();
        return process;
    }

    [Fact]
    public async Task Completion_IsDelivered_EvenIfProcessExitedBeforeSessionWasCreated()
    {
        // 回归测试：游戏进程秒退时，退出通知不能因为“订阅太晚”而丢失，否则界面会一直停在“等待游戏窗口”
        var process = StartExiting(3);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var log = Path.Combine(Path.GetTempPath(), "fcp-tests", Guid.NewGuid().ToString("N"), "game.log");

        using var session = new GameSession(null!, process, new NoWindows(), log);
        var exit = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        exit.ExitCode.ShouldBe(3);
        exit.IsCrash.ShouldBeTrue();
        exit.LastLines.ShouldContain(line => line.Trim() == "hello");  // 进程退出后才开始读取，输出也不会丢
        session.WindowShownTask.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task BeforeCompleted_RunsBeforeCompletion_AndFailuresDoNotBlockIt()
    {
        var process = StartExiting(0);
        var log = Path.Combine(Path.GetTempPath(), "fcp-tests", Guid.NewGuid().ToString("N"), "game.log");
        var saved = false;

        using var session = new GameSession(null!, process, new NoWindows(), log, async _ =>
        {
            await Task.Delay(50);
            saved = true;
            throw new IOException("磁盘已满");
        });
        var exit = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        saved.ShouldBeTrue();
        exit.ExitCode.ShouldBe(0);
        exit.IsCrash.ShouldBeFalse();
    }
}

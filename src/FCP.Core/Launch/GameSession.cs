using System.Diagnostics;
using System.Text;
using FCP.Core.Instances;
using FCP.Core.Platform;

namespace FCP.Core.Launch;

/// <summary>游戏退出的信息。</summary>
public sealed record GameExit(int ExitCode, TimeSpan Duration, bool KilledByUser, IReadOnlyList<string> LastLines, string LogFile)
{
    /// <summary>非用户主动结束、且退出码不为 0，视为崩溃（P2 接入崩溃分析）。</summary>
    public bool IsCrash => !KilledByUser && ExitCode != 0;
}

/// <summary>
/// 一次正在运行的游戏：采集输出（写入日志文件 + 保留最近的行）、检测窗口出现、处理退出。
/// </summary>
public sealed class GameSession : IDisposable
{
    private const int RecentLineLimit = 2000;
    private readonly Process _process;
    private readonly IWindowLocator _windows;
    private readonly StreamWriter _log;
    private readonly LinkedList<string> _recent = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Func<GameExit, Task>? _beforeCompleted;
    private readonly TaskCompletionSource<GameExit> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _windowShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _outputPumps;
    private bool _killedByUser;
    private bool _disposed;
    private int _exitHandled;

    /// <param name="beforeCompleted">进程退出后、<see cref="Completion"/> 完成前执行（保存游玩记录等），保证等待者看到的是最新数据。</param>
    internal GameSession(InstanceEntry entry, Process process, IWindowLocator windows, string logFile, Func<GameExit, Task>? beforeCompleted = null)
    {
        Entry = entry;
        _process = process;
        _windows = windows;
        _beforeCompleted = beforeCompleted;
        LogFile = logFile;
        StartedAt = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        _log = new StreamWriter(logFile, append: false, new UTF8Encoding(false)) { AutoFlush = true };

        process.EnableRaisingEvents = true;
        // 自己读取原始字节并逐行解码（兼容 UTF-8 与系统编码混用的输出，见 GameOutputDecoder）
        _outputPumps = Task.WhenAll(
            Task.Run(() => PumpAsync(process.StandardOutput.BaseStream)),
            Task.Run(() => PumpAsync(process.StandardError.BaseStream)));
        // 进程可能在这之前就已经退出：此时设置 EnableRaisingEvents 仍会触发 Exited；结果保存在 Completion 中，晚订阅也不会错过
        process.Exited += (_, _) => _ = OnExitedAsync();
        // 订阅之前 Exited 可能已经触发过（例如别处已开启 EnableRaisingEvents）：主动检查一次，OnExitedAsync 保证只处理一次
        if (process.HasExited)
            _ = OnExitedAsync();
        _ = WatchWindowAsync();
    }

    public InstanceEntry Entry { get; }
    public string LogFile { get; }
    public DateTimeOffset StartedAt { get; }
    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;
    public bool IsWindowShown { get; private set; }

    /// <summary>游戏进程退出（含退出码、时长和最后的输出）。随时 await 都能拿到结果，不存在“订阅太晚”的问题。</summary>
    public Task<GameExit> Completion => _completion.Task;

    /// <summary>游戏窗口出现时完成；游戏在窗口出现前退出则永不完成（请与 <see cref="Completion"/> 一起等待）。</summary>
    public Task WindowShownTask => _windowShown.Task;

    /// <summary>游戏窗口出现（可以隐藏或最小化启动器了）。</summary>
    public event EventHandler? WindowShown;

    /// <summary>收到一行游戏输出。</summary>
    public event EventHandler<string>? OutputReceived;

    /// <summary>游戏进程退出。</summary>
    public event EventHandler<GameExit>? Exited;

    public IReadOnlyList<string> RecentLines
    {
        get
        {
            lock (_recent)
                return _recent.ToList();
        }
    }

    /// <summary>结束游戏（用户主动操作，不算崩溃）。</summary>
    public void Kill()
    {
        _killedByUser = true;
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task PumpAsync(Stream stream)
    {
        try
        {
            await GameOutputDecoder.ReadLinesAsync(stream, OnLine).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 进程结束、管道关闭
        }
    }

    private void OnLine(string line)
    {
        lock (_recent)
        {
            if (_disposed)
                return;
            _recent.AddLast(line);
            if (_recent.Count > RecentLineLimit)
                _recent.RemoveFirst();
            _log.WriteLine(line);
        }
        OutputReceived?.Invoke(this, line);
    }

    private async Task WatchWindowAsync()
    {
        while (!_process.HasExited && !IsWindowShown)
        {
            await Task.Delay(500).ConfigureAwait(false);
            if (!_process.HasExited && _windows.HasVisibleWindow(_process.Id))
            {
                IsWindowShown = true;
                _windowShown.TrySetResult();
                WindowShown?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task OnExitedAsync()
    {
        if (Interlocked.Exchange(ref _exitHandled, 1) == 1)
            return;
        _stopwatch.Stop();
        // 等待输出读取完毕（游戏派生的子进程可能一直占用管道，最多等 5 秒）
        try
        {
            await _outputPumps.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        GameExit exit;
        lock (_recent)
        {
            if (!_disposed)
                _log.Flush();
            exit = new GameExit(_process.ExitCode, _stopwatch.Elapsed, _killedByUser, _recent.TakeLast(80).ToList(), LogFile);
        }
        if (_beforeCompleted is not null)
        {
            try
            {
                await _beforeCompleted(exit).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 保存记录失败不能影响退出通知（调用方负责记录日志）
            }
        }
        _completion.TrySetResult(exit);
        Exited?.Invoke(this, exit);
    }

    public void Dispose()
    {
        lock (_recent)
        {
            _disposed = true;
            _log.Dispose();
        }
        _process.Dispose();
    }
}

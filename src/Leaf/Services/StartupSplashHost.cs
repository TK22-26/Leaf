using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Leaf.Services;

/// <summary>
/// Hosts the animated startup splash in a separate Leaf process so it keeps rendering
/// while the main process is building the main window and restoring the repository.
/// </summary>
public sealed class StartupSplashHost : IDisposable
{
    private readonly EventWaitHandle _closeEvent;
    private readonly EventWaitHandle _readyEvent;
    private readonly string _closeEventName;
    private readonly string _readyEventName;
    private Process? _process;
    private int _closed;

    public StartupSplashHost()
    {
        _closeEventName = $@"Local\LeafStartupSplash_{Guid.NewGuid():N}";
        _readyEventName = $@"Local\LeafStartupSplashReady_{Guid.NewGuid():N}";
        _closeEvent = new EventWaitHandle(false, EventResetMode.ManualReset, _closeEventName);
        _readyEvent = new EventWaitHandle(false, EventResetMode.ManualReset, _readyEventName);
    }

    public async Task ShowAsync()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            return;
        }

        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--startup-splash");
        startInfo.ArgumentList.Add(_closeEventName);
        startInfo.ArgumentList.Add(_readyEventName);

        _process = Process.Start(startInfo);
        await Task.Run(() => _readyEvent.WaitOne(TimeSpan.FromSeconds(3)));
    }

    public async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        try
        {
            _closeEvent.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        var process = _process;
        if (process != null)
        {
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        _readyEvent.Dispose();
        _closeEvent.Dispose();
    }

    public void Dispose()
    {
        _ = CloseAsync();
    }
}

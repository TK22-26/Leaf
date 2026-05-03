using System.ComponentModel;
using System.Diagnostics;

namespace Leaf.Services;

/// <summary>
/// Thin wrapper around <see cref="Process.Start(ProcessStartInfo)"/> for
/// the small auxiliary spawns that don't go through
/// <see cref="IGitCommandRunner"/> — gpg, ssh, ssh-keygen, ssh-add.
/// Centralises the redirected-stdio + cancellation + Win32Exception
/// handling that was duplicated in <c>SshKeyService</c> and
/// <c>SigningToolDetector</c>.
///
/// <para>Not registered in DI — these are one-line helpers and the
/// callers are infrastructure-level themselves.</para>
/// </summary>
internal static class ProcessHelper
{
    /// <summary>
    /// Outcome of a single process spawn. <see cref="Spawned"/> false
    /// means <see cref="Process.Start(ProcessStartInfo)"/> threw
    /// <see cref="Win32Exception"/> (binary not on PATH) or the spawn
    /// was cancelled. <see cref="ExitCode"/> is the OS-reported exit
    /// status; <see cref="Output"/> is stdout + stderr concatenated so
    /// callers don't have to know which stream produced the relevant
    /// text — ssh writes greetings to stderr while ssh-keygen writes
    /// fingerprints to stdout, and most callers want both.
    /// </summary>
    public readonly record struct Result(bool Spawned, int ExitCode, string Output);

    /// <summary>
    /// Run <paramref name="exe"/> with the given args, capture stdout
    /// and stderr, return the combined output and exit code.
    /// <paramref name="environmentOverrides"/> patches the child
    /// process's env block (null value removes the key) — used to set
    /// e.g. <c>SSH_ASKPASS</c> on ssh-add invocations without leaking
    /// the value into Leaf's own process. Returns <c>Spawned=false</c>
    /// when the binary isn't on PATH.
    ///
    /// <para>Cancellation kills the underlying OS process (not just the
    /// .NET <see cref="Process"/> handle) and propagates as
    /// <see cref="OperationCanceledException"/>; callers downstream of
    /// repo-switch / dialog-close need to distinguish "command failed"
    /// from "user moved on", and Leaf's convention is that OCE always
    /// surfaces.</para>
    /// </summary>
    public static async Task<Result> RunAsync(
        string exe,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        CancellationToken cancellationToken = default)
    {
        Process? proc;
        try
        {
            proc = StartCore(exe, args, environmentOverrides: environmentOverrides);
        }
        catch (Win32Exception)
        {
            return new Result(false, -1, string.Empty);
        }
        if (proc is null) return new Result(false, -1, string.Empty);
        using (proc)
        {
            using var killOnCancel = RegisterKillOnCancel(proc, cancellationToken);
            return await CaptureAsync(proc, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Same as <see cref="RunAsync"/> but pipes <paramref name="stdinText"/>
    /// to the process's stdin. Use this only when the child genuinely
    /// reads stdin (e.g. <c>git commit -F -</c>); for env-only callers
    /// prefer <see cref="RunAsync"/>'s <c>environmentOverrides</c>
    /// parameter to avoid the cost of an unused stdin pipe.
    /// </summary>
    public static async Task<Result> RunWithStdinAsync(
        string exe,
        IReadOnlyList<string> args,
        string stdinText,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        CancellationToken cancellationToken = default)
    {
        Process? proc;
        try
        {
            proc = StartCore(exe, args, redirectStdin: true, environmentOverrides: environmentOverrides);
        }
        catch (Win32Exception)
        {
            return new Result(false, -1, string.Empty);
        }
        if (proc is null) return new Result(false, -1, string.Empty);
        using (proc)
        {
            using var killOnCancel = RegisterKillOnCancel(proc, cancellationToken);

            await proc.StandardInput.WriteAsync(stdinText.AsMemory(), cancellationToken).ConfigureAwait(false);
            await proc.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            proc.StandardInput.Close();

            return await CaptureAsync(proc, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Spawn-and-wait probe. Used for "does this binary exist on PATH"
    /// checks where output isn't interesting. Suppresses
    /// <see cref="Win32Exception"/> (the only thing this method is
    /// trying to detect); cancellation kills the process and propagates
    /// as <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<bool> CanSpawnAsync(string exe, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        Process? proc;
        try
        {
            proc = StartCore(exe, args);
        }
        catch (Win32Exception)
        {
            return false;
        }
        if (proc is null) return false;
        using (proc)
        {
            using var killOnCancel = RegisterKillOnCancel(proc, cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// Wire <paramref name="cancellationToken"/> so that cancellation
    /// kills <paramref name="proc"/> instead of leaving an orphan
    /// running. The returned registration is disposed when the caller
    /// exits its <c>using</c> scope, unhooking the kill if the process
    /// already exited normally.
    /// </summary>
    private static CancellationTokenRegistration RegisterKillOnCancel(Process proc, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled) return default;
        return cancellationToken.Register(static state =>
        {
            try { ((Process)state!).Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
            catch (System.NotSupportedException) { /* not supported on this OS */ }
            catch (Win32Exception) { /* access denied / race with normal exit */ }
        }, proc);
    }

    private static Process? StartCore(
        string exe,
        IReadOnlyList<string> args,
        bool redirectStdin = false,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = redirectStdin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
            {
                if (value is null) psi.Environment.Remove(key);
                else psi.Environment[key] = value;
            }
        }
        return Process.Start(psi);
    }

    private static async Task<Result> CaptureAsync(Process proc, CancellationToken cancellationToken)
    {
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var combined = stdout
            + (stdout.Length > 0 && stderr.Length > 0 ? "\n" : string.Empty)
            + stderr;
        return new Result(true, proc.ExitCode, combined);
    }
}

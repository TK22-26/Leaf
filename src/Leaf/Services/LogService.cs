using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Leaf.Services;

/// <summary>
/// Log verbosity level.
/// </summary>
public enum LogLevel
{
    /// <summary>Logging disabled.</summary>
    Off,
    /// <summary>Errors and warnings only.</summary>
    Normal,
    /// <summary>All messages including info and perf timing.</summary>
    Verbose
}

/// <summary>
/// Simple file logger for diagnostics.
/// Writes to %LOCALAPPDATA%\Leaf\leaf.log with automatic rotation.
/// All methods are safe to call at any level — messages below the threshold are silently dropped.
/// </summary>
public static class Log
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Leaf");
    private static readonly string LogPath = Path.Combine(LogDir, "leaf.log");
    private static readonly ConcurrentQueue<string> Queue = new();
    private static Timer? _flushTimer;
    private static readonly Stopwatch AppTimer = Stopwatch.StartNew();
    private const long MaxLogSize = 2 * 1024 * 1024; // 2 MB

    /// <summary>
    /// Current log level. Off = no-op, Normal = errors/warnings/perf, Verbose = everything.
    /// </summary>
    public static LogLevel Level { get; set; } = LogLevel.Normal;

    public static string FilePath => LogPath;

    /// <summary>
    /// Initialize the logger. Call once at app startup.
    /// </summary>
    public static void Init(LogLevel level = LogLevel.Normal)
    {
        Level = level;
        Directory.CreateDirectory(LogDir);
        RotateIfNeeded();
        _flushTimer = new Timer(_ => Flush(), null, 500, 500);

        if (Level != LogLevel.Off)
            Enqueue("INFO", $"[App] === Session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} (level={Level}) ===");
    }

    /// <summary>
    /// Performance timing — logged at Normal and Verbose.
    /// </summary>
    public static void Perf(string? area, string message, long? elapsedMs = null)
    {
        if (Level < LogLevel.Normal) return;
        var elapsed = elapsedMs.HasValue ? $" ({elapsedMs}ms)" : "";
        Enqueue("PERF", $"[{area ?? "?"}] {message}{elapsed}");
    }

    /// <summary>
    /// Informational — logged only at Verbose.
    /// </summary>
    public static void Info(string? area, string message)
    {
        if (Level < LogLevel.Verbose) return;
        Enqueue("INFO", $"[{area ?? "?"}] {message}");
    }

    /// <summary>
    /// Warning — logged at Normal and Verbose.
    /// </summary>
    public static void Warn(string? area, string message)
    {
        if (Level < LogLevel.Normal) return;
        Enqueue("WARN", $"[{area ?? "?"}] {message}");
    }

    /// <summary>
    /// Error — logged at Normal and Verbose.
    /// </summary>
    public static void Error(string? area, string message, Exception? ex = null)
    {
        if (Level < LogLevel.Normal) return;
        var detail = ex != null ? $" | {ex.GetType().Name}: {ex.Message}" : "";
        Enqueue("ERR ", $"[{area ?? "?"}] {message}{detail}");
    }

    /// <summary>
    /// Matches credentials embedded in HTTP(S) URLs: scheme://user:secret@host...
    /// The password portion is redacted in place so URL shape is preserved for
    /// debugging. Defense-in-depth: Leaf does not intentionally emit such URLs
    /// (PATs are now resolved via Leaf.AskPass.exe), but git error messages can
    /// echo back credential-bearing URLs and we must never persist the token.
    /// </summary>
    private static readonly Regex CredentialUrlPattern = new(
        @"(?<scheme>https?://)(?<user>[^:/@\s]+):(?<secret>[^@\s]+)@",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string Redact(string message)
    {
        return CredentialUrlPattern.Replace(message, "${scheme}${user}:***@");
    }

    private static void Enqueue(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} +{AppTimer.ElapsedMilliseconds,6}ms {level} {Redact(message)}";
        Queue.Enqueue(line);
    }

    private static void Flush()
    {
        if (Queue.IsEmpty) return;

        try
        {
            using var writer = new StreamWriter(LogPath, append: true);
            while (Queue.TryDequeue(out var line))
                writer.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Don't crash the app over logging — but leave a breadcrumb in
            // the debugger so silent log-flush failures are diagnosable.
            // Using Debug.WriteLine (rather than recursing through Log.*)
            // avoids a log→flush→fail→log loop.
            Debug.WriteLine($"[LogService] Flush failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogSize)
            {
                var backup = Path.Combine(LogDir, "leaf.prev.log");
                File.Copy(LogPath, backup, overwrite: true);
                File.WriteAllText(LogPath, "");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rotation is best-effort — if it fails, the log will just keep
            // growing past MaxLogSize. Report via Debug for diagnostics.
            Debug.WriteLine($"[LogService] Rotate failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Call on app shutdown to ensure all queued messages are written.
    /// </summary>
    public static void Shutdown()
    {
        _flushTimer?.Dispose();
        Flush();
    }

    /// <summary>
    /// Helper: starts a stopwatch and returns it. Use with Perf() for timing blocks.
    /// </summary>
    public static Stopwatch StartTimer() => Stopwatch.StartNew();
}

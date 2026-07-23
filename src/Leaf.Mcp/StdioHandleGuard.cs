using System.Runtime.InteropServices;

namespace Leaf.Mcp;

/// <summary>
/// Marks this process's stdin/stdout/stderr handles non-inheritable.
/// <para>
/// .NET's <c>Process.Start</c> spawns children with
/// <c>bInheritHandles=TRUE</c>, so every inheritable handle in this
/// process — including the JSON-RPC stdio pipes the MCP client gave us —
/// leaks into each spawned <c>git.exe</c>. Git-for-Windows' msys runtime
/// enumerates and queries its inherited handles during startup, and
/// querying a pipe that has a pending blocking read on it (the MCP
/// transport is always mid-read on stdin) blocks forever: git wedges
/// before ever running, the tool call never completes, and the server
/// deadlocks. Stripping HANDLE_FLAG_INHERIT from our own std handles
/// keeps the protocol pipes out of child processes entirely.
/// </para>
/// </summary>
internal static class StdioHandleGuard
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint HandleFlagInherit = 0x1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    /// <summary>Call once at startup, before the MCP transport begins reading stdin.</summary>
    public static void MakeStdHandlesNonInheritable()
    {
        foreach (var std in (ReadOnlySpan<int>)[StdInputHandle, StdOutputHandle, StdErrorHandle])
        {
            var handle = GetStdHandle(std);
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                SetHandleInformation(handle, HandleFlagInherit, 0);
            }
        }
    }
}

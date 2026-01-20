using System;

namespace Leaf.ViewModels;

public sealed class TerminalCommandExecutedEventArgs : EventArgs
{
    public TerminalCommandExecutedEventArgs(string command, int exitCode)
    {
        Command = command;
        ExitCode = exitCode;
    }

    public string Command { get; }
    public int ExitCode { get; }
}

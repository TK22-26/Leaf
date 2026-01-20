using System;

namespace Leaf.Services;

public sealed class GitCommandEventArgs : EventArgs
{
    public GitCommandEventArgs(string workingDirectory, string arguments, int exitCode, string output, string error)
    {
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        ExitCode = exitCode;
        Output = output ?? string.Empty;
        Error = error ?? string.Empty;
    }

    public string WorkingDirectory { get; }
    public string Arguments { get; }
    public int ExitCode { get; }
    public string Output { get; }
    public string Error { get; }
}

using System.Diagnostics;

namespace Leaf.Services;

public sealed class TerminalService
{
    public async Task<int> RunCommandAsync(
        string command,
        string workingDirectory,
        string shellExecutable,
        string shellArgumentsTemplate,
        Action<string>? onOutput,
        Action<string>? onError,
        CancellationToken cancellationToken)
    {
        var executable = Environment.ExpandEnvironmentVariables(shellExecutable);
        var arguments = BuildShellArguments(command, shellArgumentsTemplate);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var completion = new TaskCompletionSource<int>();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                onOutput?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                onError?.Invoke(e.Data);
            }
        };
        process.Exited += (_, _) => completion.TrySetResult(process.ExitCode);

        if (!process.Start())
        {
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using (cancellationToken.Register(() =>
               {
                   try
                   {
                       if (!process.HasExited)
                       {
                           process.Kill(entireProcessTree: true);
                       }
                   }
                   catch
                   {
                       // Ignore kill failures.
                   }
               }))
        {
            return await completion.Task.ConfigureAwait(false);
        }
    }

    private static string BuildShellArguments(string command, string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return command;
        }

        if (template.Contains("{command}", StringComparison.OrdinalIgnoreCase))
        {
            return template.Replace("{command}", command);
        }

        return $"{template} {command}";
    }
}

using System.Windows;
using Leaf.Services;

namespace Leaf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check for command-line arguments
        if (e.Args.Length > 0)
        {
            var handled = await HandleCommandLineArgsAsync(e.Args);
            if (handled)
            {
                Shutdown();
                return;
            }
        }

        // Normal startup - MainWindow is created via StartupUri in App.xaml
    }

    private static async Task<bool> HandleCommandLineArgsAsync(string[] args)
    {
        // Parse arguments
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLowerInvariant();

            switch (arg)
            {
                case "--auto-commit":
                case "-ac":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --auto-commit requires a repository name or path.");
                        Console.Error.WriteLine("Usage: Leaf.exe --auto-commit <repoName>");
                        Environment.ExitCode = 1;
                        return true;
                    }

                    var repoName = args[i + 1];
                    var (success, message) = await RunAutoCommitAsync(repoName);

                    if (success)
                    {
                        Console.WriteLine(message);
                        Environment.ExitCode = 0;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: {message}");
                        Environment.ExitCode = 1;
                    }
                    return true;

                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.ExitCode = 0;
                    return true;
            }
        }

        return false;
    }

    private static async Task<(bool Success, string Message)> RunAutoCommitAsync(string repoNameOrPath)
    {
        Console.WriteLine($"Auto-commit for repository: {repoNameOrPath}");
        Console.WriteLine();

        try
        {
            var gitService = new GitService();
            var settingsService = new SettingsService();
            var repositoryService = new RepositoryManagementService(settingsService);

            var autoCommitService = new AutoCommitService(gitService, settingsService, repositoryService);
            return await autoCommitService.AutoCommitAsync(repoNameOrPath);
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Leaf - Git Client");
        Console.WriteLine();
        Console.WriteLine("Usage: Leaf.exe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --auto-commit, -ac <repoName>  Stage all changes and commit with AI-generated message");
        Console.WriteLine("  --help, -h                     Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  Leaf.exe --auto-commit MyProject");
        Console.WriteLine("  Leaf.exe -ac \"C:\\Repos\\MyProject\"");
        Console.WriteLine();
        Console.WriteLine("Note: The repository must be added to Leaf first, and an AI provider must be configured.");
    }
}

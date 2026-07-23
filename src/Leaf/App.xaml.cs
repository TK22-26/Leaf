using System.Windows;
using Leaf.Composition;
using Leaf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Repository path to open on startup, set via --repo command-line flag.
    /// </summary>
    internal static string? InitialRepoPath { get; private set; }

    /// <summary>
    /// The app-wide DI provider. Built once in <see cref="OnStartup"/> and
    /// disposed in <see cref="OnExit"/>. Every service and view model in the
    /// app resolves through this root — the CLI path, the GUI path, and
    /// (once Phase 4 lands) per-repo scopes.
    /// </summary>
    internal static IServiceProvider Services => _provider
        ?? throw new InvalidOperationException("Service provider requested before App.OnStartup built it.");

    private static ServiceProvider? _provider;

    /// <summary>
    /// Wire the process-wide unhandled-exception sinks. Dispatcher faults
    /// (the common case — a command handler that threw) are logged,
    /// surfaced via the toast sink, and swallowed so a single stray fault
    /// doesn't tear down the whole session. AppDomain / unobserved-task
    /// faults are logged for the post-mortem (those can't be recovered).
    /// </summary>
    private void WireGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("App", "Unhandled dispatcher exception", args.Exception);
            try
            {
                AsyncErrorHandler.Handle(args.Exception, "UI operation", isUserAction: true);
            }
            catch
            {
                // The toast sink isn't initialized until the container is
                // built; the log line above is the guaranteed record.
            }
            // Keep the app alive. Truly fatal conditions (OutOfMemory,
            // StackOverflow, ExecutionEngine) are not delivered here, so
            // marking handled only ever rescues recoverable faults.
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("App", $"Unhandled AppDomain exception (terminating={args.IsTerminating})",
                args.ExceptionObject as Exception);

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("App", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        if (TryGetStartupSplashArgs(e.Args, out var splashCloseEventName, out var splashReadyEventName))
        {
            // The splash subprocess is a UI shell with no services to
            // inject — keep it off the DI path to avoid paying container
            // build cost for a bounce window.
            RunStartupSplashMode(splashCloseEventName, splashReadyEventName);
            return;
        }

        // Logging must be live before the container exists, because
        // container construction may emit log messages. Read log level
        // off disk with a throwaway SettingsService — the DI-owned
        // SettingsService is created when the provider is built.
        var logLevelSettings = new SettingsService().LoadSettings();
        var logLevel = Enum.TryParse<LogLevel>(logLevelSettings.LogLevel, true, out var parsed) ? parsed : LogLevel.Normal;
        Log.Init(logLevel);

        // Global exception backstop, wired before anything else can throw.
        // A faulted async-void command (e.g. an AsyncRelayCommand whose
        // handler let a git failure escape) rethrows on the dispatcher;
        // with no handler the process silently terminates — a Blame on a
        // file not in HEAD did exactly that. Log every such fault, surface
        // it, and keep the UI alive for recoverable ones.
        WireGlobalExceptionHandlers();

        _provider = new ServiceCollection()
            .AddLeafServices()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        // One-time composition-level wiring. Both concerns (async error
        // sink + credential migration) want the canonical instances from
        // the container, not ad-hoc copies.
        var settingsService = _provider.GetRequiredService<SettingsService>();
        var credentialService = _provider.GetRequiredService<CredentialService>();
        var notificationService = _provider.GetRequiredService<INotificationService>();

        AsyncErrorHandler.Init(
            notificationService,
            () => settingsService.LoadSettings().ShowBackgroundOperationErrors);

        settingsService.MigrateCredentialsIfNeeded(credentialService);

        // V8: keep the merge-editor palette in lockstep with the OS theme.
        // Must run before the first merge editor window renders so its
        // bindings resolve against the correct palette on first paint.
        // The settings-driven custom palette path (post-V8 closeout) is
        // passed in so an override, when present, is layered atop the
        // Dark/Light base on first paint too.
        var startupSettings = settingsService.LoadSettings();
        MergeThemeSwitcher.Initialize(startupSettings.CustomMergePalettePath);

        // Post-V8 motion closeout: push the persisted ReduceMotion preference
        // into the static gate on MergeMotionHelpers so the first merge
        // editor interaction already honours it. A future settings UI can
        // assign to MergeMotionHelpers.ReduceMotion for runtime toggles.
        Leaf.Controls.Merge.MergeMotionHelpers.ReduceMotion =
            startupSettings.ReduceMotion;

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

        StartupSplashHost? splashHost = null;

        try
        {
            splashHost = new StartupSplashHost();
            await splashHost.ShowAsync();

            var mainWindow = new MainWindow(_provider);

            MainWindow = mainWindow;
            await mainWindow.InitializeStartupAsync();

            mainWindow.Show();
            await mainWindow.WaitForFirstRenderAsync();
            await Task.Delay(500);
            await splashHost.CloseAsync();
            mainWindow.Activate();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            Log.Error("App", $"Startup failed: {ex.Message}", ex);

            if (splashHost != null)
            {
                await splashHost.CloseAsync();
            }

            Shutdown();
        }
    }

    private void RunStartupSplashMode(string splashCloseEventName, string splashReadyEventName)
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        EventWaitHandle? closeEvent = null;
        EventWaitHandle? readyEvent = null;
        RegisteredWaitHandle? waitRegistration = null;

        try
        {
            closeEvent = EventWaitHandle.OpenExisting(splashCloseEventName);
            readyEvent = EventWaitHandle.OpenExisting(splashReadyEventName);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            Shutdown();
            return;
        }

        var splashWindow = new Views.StartupSplashWindow();
        MainWindow = splashWindow;

        waitRegistration = ThreadPool.RegisterWaitForSingleObject(
            closeEvent,
            static (state, _) =>
            {
                var window = (Window)state!;
                window.Dispatcher.BeginInvoke(() =>
                {
                    window.Close();
                });
            },
            splashWindow,
            Timeout.Infinite,
            executeOnlyOnce: true);

        splashWindow.Closed += (_, _) =>
        {
            waitRegistration?.Unregister(null);
            readyEvent.Dispose();
            closeEvent.Dispose();
        };

        splashWindow.Loaded += (_, _) =>
        {
            try
            {
                readyEvent.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        };

        splashWindow.Show();
    }

    private static bool TryGetStartupSplashArgs(string[] args, out string closeEventName, out string readyEventName)
    {
        for (int i = 0; i < args.Length - 2; i++)
        {
            if (string.Equals(args[i], "--startup-splash", StringComparison.OrdinalIgnoreCase))
            {
                closeEventName = args[i + 1];
                readyEventName = args[i + 2];
                return !string.IsNullOrWhiteSpace(closeEventName) && !string.IsNullOrWhiteSpace(readyEventName);
            }
        }

        closeEventName = string.Empty;
        readyEventName = string.Empty;
        return false;
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

                case "--repo":
                case "-r":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --repo requires a repository path.");
                        Console.Error.WriteLine("Usage: Leaf.exe --repo <path>");
                        Environment.ExitCode = 1;
                        return true;
                    }

                    InitialRepoPath = args[i + 1];
                    return false; // don't shutdown — launch GUI with this repo

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
        Log.Info("App", $"Auto-commit mode started for: {repoNameOrPath}");
        Console.WriteLine($"Auto-commit for repository: {repoNameOrPath}");
        Console.WriteLine();

        try
        {
            // Resolves the same singletons the GUI path uses — killing the
            // old hand-wired duplicate composition that lived here before.
            var autoCommitService = Services.GetRequiredService<AutoCommitService>();
            return await autoCommitService.AutoCommitAsync(repoNameOrPath);
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose the container first so anything the container owns
        // (singletons implementing IDisposable) winds down before the
        // process-global Log is shut down.
        _provider?.Dispose();
        _provider = null;

        Log.Shutdown();
        base.OnExit(e);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Leaf - Git Client");
        Console.WriteLine();
        Console.WriteLine("Usage: Leaf.exe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo, -r <path>              Open Leaf and navigate to the specified repository");
        Console.WriteLine("  --auto-commit, -ac <repoName>  Stage all changes and commit with AI-generated message");
        Console.WriteLine("  --help, -h                     Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  Leaf.exe --repo \"C:\\Repos\\MyProject\"");
        Console.WriteLine("  Leaf.exe --auto-commit MyProject");
        Console.WriteLine("  Leaf.exe -ac \"C:\\Repos\\MyProject\"");
        Console.WriteLine();
        Console.WriteLine("Note: The repository must be added to Leaf first, and an AI provider must be configured.");
    }
}

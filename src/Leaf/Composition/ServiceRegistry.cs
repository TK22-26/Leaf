using System.Windows;
using Leaf.Services;
using Leaf.Services.PullRequests;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.Composition;

/// <summary>
/// Central service registration for Leaf. Owned by <c>App.OnStartup</c>;
/// everything that participates in DI is added here and nowhere else.
/// <para>
/// This file is the single source of truth for lifetimes. Changing a
/// service from singleton to scoped (or vice-versa) belongs here, and
/// should be a deliberate edit — lifetimes govern disposal order, thread
/// affinity, and leak potential.
/// </para>
/// </summary>
public static class ServiceRegistry
{
    /// <summary>
    /// Register every service and view model Leaf resolves through DI.
    /// Returns the collection so callers can chain overrides (mainly for
    /// tests that swap in fakes before <c>BuildServiceProvider</c>).
    /// </summary>
    public static IServiceCollection AddLeafServices(this IServiceCollection services)
    {
        AddInfrastructureServices(services);
        AddGitServices(services);
        AddUiServices(services);
        AddAiServices(services);
        AddRepositoryScopedServices(services);
        AddViewModels(services);

        return services;
    }

    // App-lifetime singletons. SettingsService and CredentialService are
    // concrete-only because other subsystems (CredentialHelper, legacy
    // migration) inject them directly; CredentialService has an interface
    // that GitFlowService/AutoFetchService/PullRequestService take, so we
    // forward ICredentialService to the same singleton instance. This is
    // the only place where a double-registration is needed — services
    // that have an interface and no external concrete callers are
    // registered just once as AddSingleton<I, T>.
    private static void AddInfrastructureServices(IServiceCollection services)
    {
        services.AddSingleton<SettingsService>();

        services.AddSingleton<CredentialService>();
        services.AddSingleton<ICredentialService>(sp => sp.GetRequiredService<CredentialService>());

        services.AddSingleton<FileWatcherService>();

        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IFolderWatcherService, FolderWatcherService>();
        services.AddSingleton<IWindowService, WindowService>();

        // DispatcherService needs the WPF Dispatcher. We resolve it from
        // Application.Current — available by the time OnStartup builds the
        // provider. Registered as a factory so the Dispatcher is only read
        // at first resolution, not at registration time.
        services.AddSingleton<IDispatcherService>(_ =>
            new DispatcherService(Application.Current.Dispatcher));
    }

    private static void AddGitServices(IServiceCollection services)
    {
        services.AddSingleton<IGitCommandRunner, GitCommandRunner>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<IRepositoryManagementService, RepositoryManagementService>();
        services.AddSingleton<IGitFlowService, GitFlowService>();
        services.AddSingleton<IAutoFetchService, AutoFetchService>();
        services.AddSingleton<IDiffService, DiffService>();
        services.AddSingleton<IHunkService, HunkService>();
        services.AddSingleton<Leaf.Services.Merge.IMergeEngine, Leaf.Services.Merge.GitMergeFileEngine>();
        services.AddSingleton<Leaf.Services.Merge.IWordDiffService, Leaf.Services.Merge.WordDiffService>();
        services.AddSingleton<Leaf.Services.Merge.IImageMergeService, Leaf.Services.Merge.ImageMergeService>();
        services.AddSingleton<Leaf.Services.Merge.IMergeBlameService, Leaf.Services.Merge.MergeBlameService>();
        services.AddSingleton<IGitignoreService, GitignoreService>();
        services.AddSingleton<IExternalToolDetectorService, ExternalToolDetectorService>();
        services.AddSingleton<IExternalToolConfigService, ExternalToolConfigService>();
        services.AddSingleton<IExternalToolLauncherService, ExternalToolLauncherService>();
        services.AddSingleton<IInteractiveRebaseService, InteractiveRebaseService>();
        services.AddSingleton<IPatchService, PatchService>();
        services.AddSingleton<Leaf.Services.Shortcuts.IShortcutService, Leaf.Services.Shortcuts.ShortcutService>();

        // CLI --auto-commit path. Same provider, same lifetime rules.
        services.AddSingleton<AutoCommitService>();
    }

    private static void AddUiServices(IServiceCollection services)
    {
        services.AddSingleton<IRepositoryEventHub, RepositoryEventHub>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IPullRequestService, PullRequestService>();
    }

    private static void AddAiServices(IServiceCollection services)
    {
        services.AddSingleton<OllamaService>();
        services.AddSingleton<ICommitMessageParser, CommitMessageParser>();
        services.AddSingleton<IAiCommitMessageService, AiCommitMessageService>();

        // Phase 5: AI-assisted merge resolution via MCP. The assistant itself
        // is transport + gating only; the providers read fresh values from
        // SettingsService on every invocation so a settings change takes
        // effect without reconstructing the singleton.
        services.AddSingleton<Leaf.Services.Merge.IAiMergeAssistant>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>();
            return new Leaf.Services.Merge.McpMergeAssistant(
                serverPathProvider: () =>
                {
                    var path = settings.LoadSettings().AiMergeMcpServerPath;
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                },
                enabledProvider: () => settings.LoadSettings().AiMergeEnabled,
                consentGivenProvider: () => settings.LoadSettings().AiMergeConsentGiven);
        });
    }

    // Phase 4: per-repo scope. The factory stays a singleton because its
    // job is validation + path normalization, not lifetime management.
    // IRepositorySession is scoped and constructed on first resolve via
    // the factory + the scope-local context. MEDI disposes the session
    // automatically when the scope is disposed — which is precisely what
    // the pre-DI code was doing by hand in MainViewModel.
    private static void AddRepositoryScopedServices(IServiceCollection services)
    {
        services.AddSingleton<IRepositorySessionFactory, RepositorySessionFactory>();

        services.AddScoped<RepositoryScopeContext>();
        services.AddScoped<IRepositorySession>(sp =>
        {
            var context = sp.GetRequiredService<RepositoryScopeContext>();
            if (string.IsNullOrEmpty(context.Path))
            {
                throw new InvalidOperationException(
                    "RepositoryScopeContext.Path must be set before resolving IRepositorySession. " +
                    "Set the path on the scope's RepositoryScopeContext immediately after creating the scope.");
            }

            var factory = sp.GetRequiredService<IRepositorySessionFactory>();
            return factory.Create(context.Path);
        });
    }

    // MainViewModel is per-app (one window, one VM). Child VMs stay new'd
    // inside MainViewModel today; Phase 4 moves the per-repo ones into a
    // scoped provider.
    private static void AddViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
    }
}

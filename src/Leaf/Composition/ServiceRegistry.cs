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

    // App-lifetime singletons. Where a service has both an interface and a
    // concrete type that consumers inject directly (e.g. AutoCommitService
    // takes GitService, SettingsService, RepositoryManagementService as
    // concretes), we register the concrete as the primary binding and
    // forward the interface to the same instance — so both injection
    // shapes resolve to one shared object.
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

        services.AddSingleton<GitService>();
        services.AddSingleton<IGitService>(sp => sp.GetRequiredService<GitService>());

        services.AddSingleton<RepositoryManagementService>();
        services.AddSingleton<IRepositoryManagementService>(sp => sp.GetRequiredService<RepositoryManagementService>());

        services.AddSingleton<IGitFlowService, GitFlowService>();
        services.AddSingleton<IAutoFetchService, AutoFetchService>();
        services.AddSingleton<IDiffService, DiffService>();
        services.AddSingleton<IHunkService, HunkService>();
        services.AddSingleton<IThreeWayMergeService, ThreeWayMergeService>();
        services.AddSingleton<IGitignoreService, GitignoreService>();

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
    }

    // Phase 4 replaces the factory with IServiceScopeFactory-driven session
    // resolution. Until then, keep the factory as a singleton so existing
    // callers keep working.
    private static void AddRepositoryScopedServices(IServiceCollection services)
    {
        services.AddSingleton<IRepositorySessionFactory, RepositorySessionFactory>();
    }

    // MainViewModel is per-app (one window, one VM). Child VMs stay new'd
    // inside MainViewModel today; Phase 4 moves the per-repo ones into a
    // scoped provider.
    private static void AddViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
    }
}

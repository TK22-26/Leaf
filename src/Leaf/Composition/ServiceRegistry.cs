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
        services.AddSingleton<IRebaseService, RebaseService>();
        services.AddSingleton<IWorkspaceConfigService, WorkspaceConfigService>();
        services.AddSingleton<IPatchService, PatchService>();
        services.AddSingleton<IBisectService, BisectService>();
        services.AddSingleton<Leaf.Services.Shortcuts.IShortcutService, Leaf.Services.Shortcuts.ShortcutService>();
        services.AddSingleton<IBranchColorPaletteRegistry, BranchColorPaletteRegistry>();
        services.AddSingleton<ICommitTemplateService, CommitTemplateService>();
        services.AddSingleton<Leaf.Services.Signing.ISigningToolDetector, Leaf.Services.Signing.SigningToolDetector>();
        services.AddSingleton<Leaf.Services.Ssh.ISshKeyService, Leaf.Services.Ssh.SshKeyService>();

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
        services.AddSingleton<Leaf.Services.Ai.IAiCliRunner, Leaf.Services.Ai.AiCliRunner>();
        // CLI adapters per provider — stateless wrappers that translate
        // (prompt, schema) → AiCliInvocation and unwrap the provider's
        // response envelope. Resolved into AiCommitMessageService and
        // (Phase 3) the per-provider merge assistants via IEnumerable<IAiCliAdapter>.
        services.AddSingleton<Leaf.Services.Ai.Adapters.IAiCliAdapter, Leaf.Services.Ai.Adapters.ClaudeCliAdapter>();
        services.AddSingleton<Leaf.Services.Ai.Adapters.IAiCliAdapter, Leaf.Services.Ai.Adapters.GeminiCliAdapter>();
        services.AddSingleton<Leaf.Services.Ai.Adapters.IAiCliAdapter, Leaf.Services.Ai.Adapters.CodexCliAdapter>();
        services.AddSingleton<IAiCommitMessageService, AiCommitMessageService>();

        // Direct-billing HTTP transport. Single shared HttpClient with a
        // bounded connection lifetime so DNS rotation eventually picks
        // up — auth is set per-request inside each IAiApiClient so the
        // shared instance never holds a provider-specific header on
        // DefaultRequestHeaders.
        services.AddSingleton(_ =>
        {
            var handler = new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            return new System.Net.Http.HttpClient(handler);
        });
        // One IAiApiClient registration per provider. The merge router
        // picks the right one out of IEnumerable<IAiApiClient> by
        // matching Provider, so adding the next provider is a single
        // AddSingleton + no consumer-side change.
        services.AddSingleton<Leaf.Services.Ai.Http.IAiApiClient>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>();
            var creds = sp.GetRequiredService<CredentialService>();
            var http = sp.GetRequiredService<System.Net.Http.HttpClient>();
            int Timeout() => Math.Max(1, settings.LoadSettings().AiCliTimeoutSeconds);
            return new Leaf.Services.Ai.Http.ClaudeApiClient(
                http,
                keyReader: () => creds.GetAiApiKey("Claude"),
                modelProvider: () => settings.LoadSettings().ClaudeApiModel,
                timeoutSecondsProvider: Timeout);
        });
        services.AddSingleton<Leaf.Services.Ai.Http.IAiApiClient>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>();
            var creds = sp.GetRequiredService<CredentialService>();
            var http = sp.GetRequiredService<System.Net.Http.HttpClient>();
            int Timeout() => Math.Max(1, settings.LoadSettings().AiCliTimeoutSeconds);
            return new Leaf.Services.Ai.Http.GeminiApiClient(
                http,
                keyReader: () => creds.GetAiApiKey("Gemini"),
                modelProvider: () => settings.LoadSettings().GeminiApiModel,
                timeoutSecondsProvider: Timeout);
        });

        // AI-assisted merge resolution. The router holds one of every
        // provider implementation and dispatches to whichever is selected
        // in AppSettings.AiMergeProvider — re-read on every call so a
        // settings change takes effect without DI rebuild. All inner
        // providers read settings via Func<T> closures, so the router
        // is stateless and safe as a singleton.
        services.AddSingleton<Leaf.Services.Merge.IAiMergeAssistant>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>();
            var runner = sp.GetRequiredService<Leaf.Services.Ai.IAiCliRunner>();
            var ollama = sp.GetRequiredService<OllamaService>();
            var adapters = sp.GetServices<Leaf.Services.Ai.Adapters.IAiCliAdapter>().ToList();
            var claudeAdapter = (Leaf.Services.Ai.Adapters.ClaudeCliAdapter)adapters.First(a => a.Provider == Leaf.Services.Merge.AiProviderKind.Claude);
            var geminiAdapter = (Leaf.Services.Ai.Adapters.GeminiCliAdapter)adapters.First(a => a.Provider == Leaf.Services.Merge.AiProviderKind.Gemini);
            var codexAdapter = (Leaf.Services.Ai.Adapters.CodexCliAdapter)adapters.First(a => a.Provider == Leaf.Services.Merge.AiProviderKind.Codex);

            int Timeout() => Math.Max(1, settings.LoadSettings().AiCliTimeoutSeconds);
            bool Enabled() => settings.LoadSettings().AiMergeEnabled;
            bool Consent() => settings.LoadSettings().AiMergeConsentGiven;

            var claude = new Leaf.Services.Merge.Providers.ClaudeMergeAssistant(
                runner, claudeAdapter, Enabled, Consent,
                () => settings.LoadSettings().IsClaudeConnected, Timeout);
            var gemini = new Leaf.Services.Merge.Providers.GeminiMergeAssistant(
                runner, geminiAdapter, Enabled, Consent,
                () => settings.LoadSettings().IsGeminiConnected, Timeout);
            var codex = new Leaf.Services.Merge.Providers.CodexMergeAssistant(
                runner, codexAdapter, Enabled, Consent,
                () => settings.LoadSettings().IsCodexConnected, Timeout);
            var ollamaAssistant = new Leaf.Services.Merge.Providers.OllamaMergeAssistant(
                ollama, Enabled, Consent,
                () => settings.LoadSettings().OllamaBaseUrl,
                () => settings.LoadSettings().OllamaSelectedModel,
                Timeout);
            var externalServer = new Leaf.Services.Merge.ExternalServerMergeAssistant(
                serverPathProvider: () =>
                {
                    var path = settings.LoadSettings().AiMergeExternalServerPath;
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                },
                enabledProvider: Enabled,
                consentGivenProvider: Consent);

            // API-key variants. The router resolves them by matching
            // IAiApiClient.Provider, so the DI registration order of
            // those clients doesn't matter here.
            var apiClients = sp.GetServices<Leaf.Services.Ai.Http.IAiApiClient>().ToList();
            var claudeApiClient = apiClients.First(c => c.Provider == Leaf.Services.Merge.AiProviderKind.ClaudeApi);
            var geminiApiClient = apiClients.First(c => c.Provider == Leaf.Services.Merge.AiProviderKind.GeminiApi);

            var claudeApi = new Leaf.Services.Merge.Providers.ClaudeApiMergeAssistant(
                claudeApiClient, Enabled, Consent,
                () => settings.LoadSettings().IsClaudeApiConnected);
            var geminiApi = new Leaf.Services.Merge.Providers.GeminiApiMergeAssistant(
                geminiApiClient, Enabled, Consent,
                () => settings.LoadSettings().IsGeminiApiConnected);

            return new Leaf.Services.Merge.AiMergeAssistantRouter(
                selectedProviderProvider: () => settings.LoadSettings().AiMergeProvider,
                enabledProvider: Enabled,
                consentProvider: Consent,
                claude, gemini, codex, ollamaAssistant, externalServer, claudeApi, geminiApi);
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
        // Workspace VM is per-host-VM so it shares MainViewModel's
        // lifetime. Singleton matches that — the host owns it directly
        // and rebuilds its tile set on every grid-mode entry.
        services.AddSingleton<WorkspaceViewModel>();
    }
}

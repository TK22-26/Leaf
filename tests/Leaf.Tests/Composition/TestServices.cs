using Leaf.Composition;
using Leaf.Services;
using Leaf.Tests.Fakes;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leaf.Tests.Composition;

/// <summary>
/// Helpers for building a DI provider in unit tests.
/// <para>
/// The default flow starts from <see cref="ServiceRegistry.AddLeafServices"/>
/// so tests exercise the production wiring, then swaps a fixed set of
/// I/O-bound singletons (git, dialog, dispatcher, filesystem, clipboard,
/// AI, gitignore) for predictable fakes. Tests that want different
/// fakes or real implementations override them with <c>Replace</c> or
/// <c>AddSingleton</c> before calling <see cref="BuildProvider"/>.
/// </para>
/// </summary>
public static class TestServices
{
    /// <summary>
    /// Returns a ServiceCollection pre-wired with Leaf's production
    /// registrations, with I/O-bound services replaced by fakes so tests
    /// are hermetic.
    /// </summary>
    public static IServiceCollection CreateCollection()
    {
        var services = new ServiceCollection()
            .AddLeafServices();

        services.Replace(ServiceDescriptor.Singleton<IDispatcherService, FakeDispatcherService>());
        services.Replace(ServiceDescriptor.Singleton<IGitService, FakeGitService>());
        services.Replace(ServiceDescriptor.Singleton<IDialogService, FakeDialogService>());
        services.Replace(ServiceDescriptor.Singleton<IFileSystemService, FakeFileSystemService>());
        services.Replace(ServiceDescriptor.Singleton<IClipboardService, FakeClipboardService>());
        services.Replace(ServiceDescriptor.Singleton<IAiCommitMessageService, FakeAiCommitMessageService>());
        services.Replace(ServiceDescriptor.Singleton<IGitignoreService, FakeGitignoreService>());

        // Test-only VM registrations. These are constructed by MainViewModel
        // in production, not resolved from DI, so they're not in the
        // production registry — but making them transient here lets tests
        // pull a fully-wired VM straight out of the provider.
        services.AddTransient<WorkingChangesViewModel>();

        return services;
    }

    /// <summary>
    /// Build a validated <see cref="ServiceProvider"/>. <c>ValidateScopes</c>
    /// and <c>ValidateOnBuild</c> are on so registration bugs surface at
    /// test time rather than in production.
    /// </summary>
    public static ServiceProvider BuildProvider(IServiceCollection services)
    {
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}

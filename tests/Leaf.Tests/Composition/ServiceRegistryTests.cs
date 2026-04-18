using FluentAssertions;
using Leaf.Composition;
using Leaf.Services;
using Leaf.Tests.Fakes;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Leaf.Tests.Composition;

/// <summary>
/// Phase 2 gate: the DI registry must resolve every top-level service the
/// app depends on. The real IDispatcherService requires
/// Application.Current.Dispatcher, which isn't available in a unit-test
/// context; tests swap in the FakeDispatcherService before resolving.
/// </summary>
public class ServiceRegistryTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddLeafServices();

        // Replace the WPF Dispatcher-backed registration with a fake so
        // tests don't need an Application instance running.
        services.RemoveAll<IDispatcherService>();
        services.AddSingleton<IDispatcherService, FakeDispatcherService>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void Registry_Resolves_MainViewModel()
    {
        using var provider = BuildProvider();
        var sut = provider.GetRequiredService<MainViewModel>();
        sut.Should().NotBeNull();
    }

    [Fact]
    public void Registry_Resolves_AutoCommitService()
    {
        using var provider = BuildProvider();
        var sut = provider.GetRequiredService<AutoCommitService>();
        sut.Should().NotBeNull();
    }

    [Theory]
    [InlineData(typeof(IGitService))]
    [InlineData(typeof(IGitFlowService))]
    [InlineData(typeof(IGitCommandRunner))]
    [InlineData(typeof(IDiffService))]
    [InlineData(typeof(IHunkService))]
    [InlineData(typeof(Leaf.Services.Merge.IMergeEngine))]
    [InlineData(typeof(IGitignoreService))]
    [InlineData(typeof(IAutoFetchService))]
    [InlineData(typeof(IRepositoryManagementService))]
    [InlineData(typeof(IAiCommitMessageService))]
    [InlineData(typeof(ICommitMessageParser))]
    [InlineData(typeof(IDialogService))]
    [InlineData(typeof(INotificationService))]
    [InlineData(typeof(IRepositoryEventHub))]
    [InlineData(typeof(IClipboardService))]
    [InlineData(typeof(IFileSystemService))]
    [InlineData(typeof(IFolderWatcherService))]
    [InlineData(typeof(IWindowService))]
    [InlineData(typeof(IRepositorySessionFactory))]
    [InlineData(typeof(Leaf.Services.PullRequests.IPullRequestService))]
    [InlineData(typeof(SettingsService))]
    [InlineData(typeof(CredentialService))]
    public void Registry_Resolves(System.Type serviceType)
    {
        using var provider = BuildProvider();
        var instance = provider.GetRequiredService(serviceType);
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Registry_ReturnsSameInstance_ForSingletonService()
    {
        using var provider = BuildProvider();
        var a = provider.GetRequiredService<IGitService>();
        var b = provider.GetRequiredService<IGitService>();
        a.Should().BeSameAs(b);
    }
}

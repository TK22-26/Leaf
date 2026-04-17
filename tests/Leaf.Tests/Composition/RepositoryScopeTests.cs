using FluentAssertions;
using Leaf.Composition;
using Leaf.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leaf.Tests.Composition;

/// <summary>
/// Phase 4 gate: repository scopes must bind one IRepositorySession per
/// scope and let the caller feed the repo path in before the session is
/// resolved. These tests exercise the scope contract directly without
/// needing a real git repo — the session factory bails if the path is
/// missing, which is the only invariant the tests need to prove.
/// </summary>
public class RepositoryScopeTests
{
    private static ServiceProvider BuildProvider()
        => TestServices.BuildProvider(TestServices.CreateCollection());

    [Fact]
    public void Session_WithoutPath_ThrowsInvalidOperation()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Path was never set on the scope context — resolving the
        // session must surface the misuse rather than returning a
        // half-built object.
        var act = () => scope.ServiceProvider.GetRequiredService<IRepositorySession>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RepositoryScopeContext.Path*");
    }

    [Fact]
    public void Session_WithBogusPath_ThrowsFromFactory()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<RepositoryScopeContext>().Path = "C:/definitely/not/a/repo/xyz";

        // Factory delegates to IRepositorySessionFactory.Create, which
        // throws ArgumentException for a non-repo path. The exception
        // surfaces through GetRequiredService.
        var act = () => scope.ServiceProvider.GetRequiredService<IRepositorySession>();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DifferentScopes_ResolveDifferentContexts()
    {
        using var provider = BuildProvider();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        scopeA.ServiceProvider.GetRequiredService<RepositoryScopeContext>().Path = "A";
        scopeB.ServiceProvider.GetRequiredService<RepositoryScopeContext>().Path = "B";

        // Contexts are scoped — sibling scopes must not share state.
        scopeA.ServiceProvider.GetRequiredService<RepositoryScopeContext>().Path.Should().Be("A");
        scopeB.ServiceProvider.GetRequiredService<RepositoryScopeContext>().Path.Should().Be("B");
    }

    [Fact]
    public void SameScope_ResolvesContextAsSingleton()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var a = scope.ServiceProvider.GetRequiredService<RepositoryScopeContext>();
        var b = scope.ServiceProvider.GetRequiredService<RepositoryScopeContext>();

        // Within a scope, repeated resolves hand back the same instance —
        // otherwise the path set by MainViewModel would be invisible to
        // the session factory delegate.
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Context_Path_DefaultsToNull()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<RepositoryScopeContext>();

        context.Path.Should().BeNull();
    }
}

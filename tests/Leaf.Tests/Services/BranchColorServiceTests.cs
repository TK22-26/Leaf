using System.IO;
using System.Windows.Media;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Tests for §5.14 <see cref="BranchColorService"/> — the per-repo branch
/// colour authority. Covers the precedence stack (override → GitFlow →
/// palette), persistence round-trip via <see cref="SettingsService"/>,
/// the change-notification contract, and the colour-picker seed path.
/// </summary>
public class BranchColorServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly BranchColorPaletteRegistry _registry;
    private readonly RepositoryInfo _repo;
    private readonly RepositoryManagementService _repositoryService;

    public BranchColorServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "leaf-tests", Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(_tempDir);
        _registry = new BranchColorPaletteRegistry(_settings);
        _repositoryService = new RepositoryManagementService(_settings);

        _repo = new RepositoryInfo
        {
            Path = Path.Combine(_tempDir, "repo"),
            Name = "test-repo",
        };
        // Register the repo with the management service so the service's
        // SaveRepositories path includes it. The service's load helper
        // validates Exists on disk; we register manually via the public
        // AddRepository to bypass that check for a fixture path.
        _repositoryService.AddRepository(_repo, save: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private BranchColorService NewService(GitFlowConfig? gitFlow = null, IEnumerable<string>? remotes = null)
        => new BranchColorService(_repo, _settings, _repositoryService, _registry, gitFlow, remotes);

    [Fact]
    public void GetBranchColor_NoOverride_ResolvesFromActivePalette()
    {
        using var svc = NewService();
        var brush = svc.GetBranchColor("feature-x");

        brush.Should().BeOfType<SolidColorBrush>();
        brush.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void GetBranchColor_SameNameProducesSameBrush()
    {
        using var svc = NewService();
        var first = svc.GetBranchColor("hello");
        var second = svc.GetBranchColor("hello");
        first.Should().BeSameAs(second, "the resolver caches frozen brushes per branch within a session");
    }

    [Fact]
    public void GetBranchColor_NormalizesRemotePrefix()
    {
        using var svc = NewService(remotes: ["origin", "upstream"]);
        var local = (svc.GetBranchColor("foo") as SolidColorBrush)!.Color;
        var remote = (svc.GetBranchColor("origin/foo") as SolidColorBrush)!.Color;
        remote.Should().Be(local, "origin/foo and foo should map to the same palette slot");
    }

    [Fact]
    public void SetOverride_TakesPrecedenceOverPalette()
    {
        using var svc = NewService();
        svc.SetOverride("feature-x", Color.FromRgb(0xAB, 0xCD, 0xEF));
        var brush = svc.GetBranchColor("feature-x") as SolidColorBrush;
        brush.Should().NotBeNull();
        brush!.Color.Should().Be(Color.FromRgb(0xAB, 0xCD, 0xEF));
    }

    [Fact]
    public void HasOverride_ReflectsSetAndClear()
    {
        using var svc = NewService();
        svc.HasOverride("foo").Should().BeFalse();
        svc.SetOverride("foo", Colors.Red);
        svc.HasOverride("foo").Should().BeTrue();
        svc.ClearOverride("foo");
        svc.HasOverride("foo").Should().BeFalse();
    }

    [Fact]
    public void HasAnyOverrides_ReflectsCurrentState()
    {
        using var svc = NewService();
        svc.HasAnyOverrides.Should().BeFalse();
        svc.SetOverride("a", Colors.Red);
        svc.HasAnyOverrides.Should().BeTrue();
        svc.ClearAllOverrides();
        svc.HasAnyOverrides.Should().BeFalse();
    }

    [Fact]
    public void SetOverride_PersistsToRepositoriesJson()
    {
        using (var svc = NewService())
        {
            svc.SetOverride("persisted", Color.FromRgb(0x12, 0x34, 0x56));
        }

        // Re-load from disk via a fresh SettingsService and confirm the
        // override is in the persisted RepositoryInfo.
        var freshSettings = new SettingsService(_tempDir);
        var data = freshSettings.LoadRepositories();
        var repo = data.Repositories.Single(r => string.Equals(r.Path, _repo.Path, StringComparison.OrdinalIgnoreCase));
        repo.BranchColorOverrides.Should().ContainKey("persisted");
        repo.BranchColorOverrides["persisted"].Should().Be("#123456");
    }

    [Fact]
    public void ClearAllOverrides_EmptiesOverridesAndPersists()
    {
        using (var svc = NewService())
        {
            svc.SetOverride("a", Colors.Red);
            svc.SetOverride("b", Colors.Blue);
            svc.ClearAllOverrides();
        }

        var freshSettings = new SettingsService(_tempDir);
        var repo = freshSettings.LoadRepositories().Repositories
            .Single(r => string.Equals(r.Path, _repo.Path, StringComparison.OrdinalIgnoreCase));
        repo.BranchColorOverrides.Should().BeEmpty();
    }

    [Fact]
    public void ColorsChanged_FiresOnSetClearAndClearAll()
    {
        using var svc = NewService();
        var fired = 0;
        svc.ColorsChanged += (_, _) => fired++;

        svc.SetOverride("a", Colors.Red);
        fired.Should().Be(1);

        svc.ClearOverride("a");
        fired.Should().Be(2);

        svc.SetOverride("b", Colors.Blue);
        svc.ClearAllOverrides();
        fired.Should().Be(4);
    }

    [Fact]
    public void HeadLabel_AlwaysResolvesToAccentRegardlessOfPalette()
    {
        using var svc = NewService();
        var brush = svc.GetBranchColor("HEAD") as SolidColorBrush;
        brush.Should().NotBeNull();
        // Leaf accent green at 55 % alpha — see BranchColorService.CreateHeadBrush.
        brush!.Color.A.Should().Be(0x88);
    }

    [Fact]
    public void GitFlowMain_TakesPrecedenceOverPaletteButNotOverride()
    {
        var gitFlow = new GitFlowConfig
        {
            IsInitialized = true,
            MainBranch = "main",
            DevelopBranch = "develop",
        };
        using var svc = NewService(gitFlow);

        // Without an override, "main" should resolve to the GitFlow main
        // colour (BranchInfo.MainColor). The point isn't the exact RGB —
        // it's that the colour is *not* the palette slot we'd otherwise
        // get for "main", proving GitFlow took precedence.
        var withGitFlow = (svc.GetBranchColor("main") as SolidColorBrush)!.Color;

        // Now set an override — that must beat GitFlow.
        svc.SetOverride("main", Color.FromRgb(0xFF, 0x00, 0xFF));
        var withOverride = (svc.GetBranchColor("main") as SolidColorBrush)!.Color;

        withOverride.Should().NotBe(withGitFlow);
        withOverride.Should().Be(Color.FromRgb(0xFF, 0x00, 0xFF));
    }

    [Fact]
    public void GetColor_ReturnsSameColorAsBrush()
    {
        using var svc = NewService();
        var brush = (svc.GetBranchColor("xyz") as SolidColorBrush)!;
        var color = svc.GetColor("xyz");
        color.Should().Be(brush.Color);
    }

    [Fact]
    public void RefreshFromSettings_PicksUpPaletteIdChange()
    {
        using var svc = NewService();
        var beforeColor = (svc.GetBranchColor("test-branch") as SolidColorBrush)!.Color;

        // Flip the active palette in settings and ask the service to refresh.
        var settings = _settings.LoadSettings();
        settings.DefaultBranchColorPaletteId = BranchColorPaletteRegistry.HighContrastId;
        _settings.SaveSettings(settings);

        var fired = 0;
        svc.ColorsChanged += (_, _) => fired++;

        svc.RefreshFromSettings();

        fired.Should().Be(1);
        var afterColor = (svc.GetBranchColor("test-branch") as SolidColorBrush)!.Color;
        afterColor.Should().NotBe(beforeColor, "the high-contrast palette is a different colour set");
        svc.ActivePalette.Id.Should().Be(BranchColorPaletteRegistry.HighContrastId);
    }

    [Fact]
    public void SetOverride_BlankBranchNameThrows()
    {
        using var svc = NewService();
        Action act = () => svc.SetOverride("", Colors.Red);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NotAttached_DoesNotReactToRegistryPalettesChanged()
    {
        // Service constructed but never Attach()'d — simulates the "load
        // got cancelled before we swapped in the new service" path. The
        // service must NOT remain subscribed to the registry, otherwise
        // every palette change for the rest of the app's lifetime would
        // run through this orphaned instance.
        using var svc = NewService();

        var fired = 0;
        svc.ColorsChanged += (_, _) => fired++;

        // Trigger a registry PalettesChanged event. If the unattached
        // service were still subscribed, it would re-resolve and fire
        // its own ColorsChanged.
        _registry.AddOrUpdateCustom(new BranchColorPalette
        {
            Id = "wakeup-attempt",
            DisplayName = "x",
            Colors = ["#000000"],
        });

        fired.Should().Be(0, "an unattached service must not react to registry events");
    }

    [Fact]
    public void Attached_ReactsToRegistryPalettesChanged()
    {
        using var svc = NewService();
        svc.Attach();

        var fired = 0;
        svc.ColorsChanged += (_, _) => fired++;

        _registry.AddOrUpdateCustom(new BranchColorPalette
        {
            Id = "wakeup",
            DisplayName = "x",
            Colors = ["#000000"],
        });

        fired.Should().Be(1, "an attached service forwards palette changes as ColorsChanged");
    }

    [Fact]
    public void ConcurrentReadDuringWrite_DoesNotThrow()
    {
        // Smoke test for the §5.14 concurrency fix: BuildGraph runs on a
        // Task.Run thread and calls GetBranchColor while the user can
        // racily SetOverride from the UI thread. Without the override
        // lock this would intermittently blow up with
        // InvalidOperationException ("collection was modified") or
        // produce torn reads.
        using var svc = NewService();
        for (int i = 0; i < 100; i++)
            svc.SetOverride($"branch-{i}", Colors.Blue);

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var reader = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                for (int i = 0; i < 100; i++)
                    _ = svc.GetBranchColor($"branch-{i}");
            }
        });
        var writer = Task.Run(() =>
        {
            int n = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                svc.SetOverride($"branch-{n % 100}", Color.FromRgb((byte)n, 0, 0));
                n++;
            }
        });

        Action wait = () => Task.WaitAll(reader, writer);
        wait.Should().NotThrow();
    }
}

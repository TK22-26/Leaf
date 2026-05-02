using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Tests for §5.15 <see cref="CommitTemplateService"/> — placeholder
/// resolver, ticket-extraction regex, CRUD, scope behaviour, and
/// per-repo storage round-trip.
/// </summary>
public class CommitTemplateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly CommitTemplateService _service;

    public CommitTemplateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "leaf-tests", Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(_tempDir);
        _service = new CommitTemplateService(_settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void GetAll_IncludesShippedPresets()
    {
        var all = _service.GetAll();
        all.Should().NotBeEmpty();
        all.Should().Contain(t => t.Id == CommitTemplatePresets.ConventionalCommitsId);
        all.Should().Contain(t => t.Id == CommitTemplatePresets.SignedOffById);
        all.Where(t => t.IsBuiltIn).Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void GetById_UnknownReturnsNull()
    {
        _service.GetById("does-not-exist").Should().BeNull();
        _service.GetById(null).Should().BeNull();
        _service.GetById("").Should().BeNull();
    }

    [Fact]
    public void Resolve_SubstitutesBranchAndDate()
    {
        var template = new CommitTemplate
        {
            Id = "t",
            Name = "test",
            Body = "{branch} on {date}",
        };
        var resolved = _service.Resolve(template, "feature/login", null, null, out _);
        resolved.Should().StartWith("feature/login on ");
        resolved.Should().MatchRegex(@"^feature/login on \d{4}-\d{2}-\d{2}$");
    }

    [Fact]
    public void Resolve_PreservesUnknownTokens()
    {
        var template = new CommitTemplate { Id = "t", Name = "t", Body = "{not-a-token}" };
        var resolved = _service.Resolve(template, null, null, null, out _);
        resolved.Should().Be("{not-a-token}");
    }

    [Fact]
    public void Resolve_UserNameAndEmailSubstitution()
    {
        var template = new CommitTemplate { Id = "t", Name = "t", Body = "{user.name} <{user.email}>" };
        var resolved = _service.Resolve(template, null, "Tim K", "tim@example.com", out _);
        resolved.Should().Be("Tim K <tim@example.com>");
    }

    [Fact]
    public void Resolve_NullUserResolvesToEmpty()
    {
        var template = new CommitTemplate { Id = "t", Name = "t", Body = "X{user.name}Y" };
        var resolved = _service.Resolve(template, null, null, null, out _);
        resolved.Should().Be("XY");
    }

    [Fact]
    public void Resolve_CursorTokenRecordsOffsetAndIsRemoved()
    {
        var template = new CommitTemplate { Id = "t", Name = "t", Body = "before{cursor}after" };
        var resolved = _service.Resolve(template, null, null, null, out var cursor);
        resolved.Should().Be("beforeafter");
        cursor.Should().Be("before".Length);
    }

    [Fact]
    public void Resolve_NoCursorTokenReportsEndOffset()
    {
        var template = new CommitTemplate { Id = "t", Name = "t", Body = "hello" };
        _service.Resolve(template, null, null, null, out var cursor);
        cursor.Should().Be("hello".Length);
    }

    [Fact]
    public void Resolve_TicketExtractionFirstCaptureGroup()
    {
        var template = new CommitTemplate
        {
            Id = "t",
            Name = "t",
            Body = "{ticket}",
            TicketRegex = @"^feature/([A-Z]+-\d+)-",
        };
        var resolved = _service.Resolve(template, "feature/JIRA-123-do-stuff", null, null, out _);
        resolved.Should().Be("JIRA-123");
    }

    [Fact]
    public void Resolve_TicketExtractionFallsBackToWholeMatch()
    {
        var template = new CommitTemplate
        {
            Id = "t",
            Name = "t",
            Body = "{ticket}",
            TicketRegex = @"[A-Z]{3}-\d+",
        };
        var resolved = _service.Resolve(template, "topic/ABC-42", null, null, out _);
        resolved.Should().Be("ABC-42");
    }

    [Fact]
    public void Resolve_TicketExtractionMissingMatchReturnsEmpty()
    {
        var template = new CommitTemplate
        {
            Id = "t",
            Name = "t",
            Body = "[{ticket}] thing",
            TicketRegex = @"^([A-Z]+-\d+)",
        };
        var resolved = _service.Resolve(template, "main", null, null, out _);
        resolved.Should().Be("[] thing");
    }

    [Fact]
    public void Resolve_TicketExtractionInvalidRegexResolvesToEmpty()
    {
        var template = new CommitTemplate
        {
            Id = "t",
            Name = "t",
            Body = "{ticket}",
            TicketRegex = "[unclosed",
        };
        var resolved = _service.Resolve(template, "feature/x", null, null, out _);
        resolved.Should().Be(string.Empty);
    }

    [Fact]
    public void AddOrUpdate_GlobalCustomPersistsAndReloads()
    {
        var template = new CommitTemplate
        {
            Id = "global-1",
            Name = "My template",
            Body = "hello",
            Scope = CommitTemplateScope.Global,
        };
        _service.AddOrUpdate(template);

        var fresh = new CommitTemplateService(_settings);
        fresh.GetById("global-1").Should().NotBeNull();
        fresh.GetById("global-1")!.Name.Should().Be("My template");
    }

    [Fact]
    public void AddOrUpdate_PresetTweakStoredAsOverride()
    {
        // Tweak the body of the Conventional Commits preset.
        var preset = _service.GetById(CommitTemplatePresets.ConventionalCommitsId)!;
        var tweaked = new CommitTemplate
        {
            Id = preset.Id,
            Name = preset.Name,
            Body = "tweaked body",
            TicketRegex = preset.TicketRegex,
            Scope = preset.Scope,
            IsBuiltIn = preset.IsBuiltIn,
        };
        _service.AddOrUpdate(tweaked);

        // Reload from disk — the preset should now report the tweaked body
        // but still claim IsBuiltIn=true.
        var fresh = new CommitTemplateService(_settings);
        var reloaded = fresh.GetById(preset.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Body.Should().Be("tweaked body");
        reloaded.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void Delete_OnPresetIdRevertsToShippedDefault()
    {
        var preset = _service.GetById(CommitTemplatePresets.ConventionalCommitsId)!;
        var originalBody = preset.Body;

        _service.AddOrUpdate(new CommitTemplate
        {
            Id = preset.Id,
            Name = preset.Name,
            Body = "tweaked",
        });
        _service.GetById(preset.Id)!.Body.Should().Be("tweaked");

        _service.Delete(preset.Id);
        _service.GetById(preset.Id)!.Body.Should().Be(originalBody);
    }

    [Fact]
    public void Delete_OnUserGlobalRemovesEntry()
    {
        _service.AddOrUpdate(new CommitTemplate { Id = "g", Name = "g", Body = "x" });
        _service.GetById("g").Should().NotBeNull();
        _service.Delete("g");
        _service.GetById("g").Should().BeNull();
    }

    [Fact]
    public void ResetToDefaults_DropsCustomGlobalsAndOverrides()
    {
        _service.AddOrUpdate(new CommitTemplate { Id = "g", Name = "g", Body = "x" });
        _service.AddOrUpdate(new CommitTemplate
        {
            Id = CommitTemplatePresets.ConventionalCommitsId,
            Name = "tweak",
            Body = "tweaked",
        });

        _service.ResetToDefaults();

        _service.GetById("g").Should().BeNull();
        var preset = _service.GetById(CommitTemplatePresets.ConventionalCommitsId)!;
        preset.Body.Should().NotBe("tweaked", "ResetToDefaults must drop preset overrides");
    }

    [Fact]
    public void TemplatesChanged_FiresOnAddUpdateAndDelete()
    {
        var fired = 0;
        _service.TemplatesChanged += (_, _) => fired++;

        _service.AddOrUpdate(new CommitTemplate { Id = "x", Name = "x", Body = "y" });
        fired.Should().Be(1);

        _service.AddOrUpdate(new CommitTemplate { Id = "x", Name = "x2", Body = "y" });
        fired.Should().Be(2);

        _service.Delete("x");
        fired.Should().Be(3);
    }

    [Fact]
    public void RepositoryScope_RoundtripsThroughDotGitLeafJson()
    {
        var repoDir = Path.Combine(_tempDir, "fake-repo");
        Directory.CreateDirectory(Path.Combine(repoDir, ".git"));

        _service.SetActiveRepository(repoDir);
        _service.AddOrUpdate(new CommitTemplate
        {
            Id = "repo-1",
            Name = "Repo template",
            Body = "scoped body",
            Scope = CommitTemplateScope.Repository,
        });

        // File should exist on disk under .git/leaf
        var path = Path.Combine(repoDir, ".git", "leaf", "commit-templates.json");
        File.Exists(path).Should().BeTrue();

        // Fresh service pointing at the same repo should see the entry.
        var fresh = new CommitTemplateService(_settings);
        fresh.SetActiveRepository(repoDir);
        var reloaded = fresh.GetById("repo-1");
        reloaded.Should().NotBeNull();
        reloaded!.Scope.Should().Be(CommitTemplateScope.Repository);
        reloaded.IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public void RepositoryScope_NoRepoActiveRejectsRepositoryScopeAdd()
    {
        _service.SetActiveRepository(null);
        Action act = () => _service.AddOrUpdate(new CommitTemplate
        {
            Id = "no-home",
            Name = "homeless",
            Body = "x",
            Scope = CommitTemplateScope.Repository,
        });
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SwitchActiveRepository_DoesNotLeakPreviousRepoTemplates()
    {
        var repoA = Path.Combine(_tempDir, "repo-a");
        Directory.CreateDirectory(Path.Combine(repoA, ".git"));
        var repoB = Path.Combine(_tempDir, "repo-b");
        Directory.CreateDirectory(Path.Combine(repoB, ".git"));

        _service.SetActiveRepository(repoA);
        _service.AddOrUpdate(new CommitTemplate
        {
            Id = "a-only",
            Name = "Repo A",
            Body = "x",
            Scope = CommitTemplateScope.Repository,
        });

        _service.SetActiveRepository(repoB);
        _service.GetById("a-only").Should().BeNull("repo-A's template must not leak into repo-B");

        _service.SetActiveRepository(repoA);
        _service.GetById("a-only").Should().NotBeNull("returning to repo A re-loads its template");
    }

    [Fact]
    public void AddOrUpdate_BlankIdOrNameThrows()
    {
        Action blankId = () => _service.AddOrUpdate(new CommitTemplate { Id = "", Name = "n", Body = "" });
        Action blankName = () => _service.AddOrUpdate(new CommitTemplate { Id = "x", Name = "", Body = "" });
        blankId.Should().Throw<ArgumentException>();
        blankName.Should().Throw<ArgumentException>();
    }
}

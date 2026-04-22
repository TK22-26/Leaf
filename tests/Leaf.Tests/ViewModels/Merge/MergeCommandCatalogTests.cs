#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Windows.Input;
using FluentAssertions;
using Leaf.Models.Merge;
using Leaf.Services.Merge;
using Leaf.Tests.Fakes;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Verifies <see cref="MergeCommandCatalog.BuildFor"/> enumerates every
/// user-invokable command on <see cref="MergeEditorViewModel"/>. Guards
/// against silent drift when new commands are added to the VM without a
/// corresponding palette entry — the palette would forever miss them.
/// </summary>
public class MergeCommandCatalogTests
{
    [Fact]
    public void BuildFor_NullVm_Throws()
    {
        FluentActions.Invoking(() => MergeCommandCatalog.BuildFor(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildFor_IncludesCoreNavigationCommands()
    {
        var vm = CreateVm();
        var items = MergeCommandCatalog.BuildFor(vm);

        items.Select(i => i.DisplayName).Should().Contain(new[]
        {
            "Next conflict",
            "Previous conflict",
            "Next change span",
            "Previous change span",
            "Next auto-merged region",
            "Previous auto-merged region",
        });
    }

    [Fact]
    public void BuildFor_IncludesResolutionCommands()
    {
        var vm = CreateVm();
        var items = MergeCommandCatalog.BuildFor(vm);

        items.Select(i => i.DisplayName).Should().Contain(new[]
        {
            "Accept current conflict: Ours",
            "Accept current conflict: Theirs",
            "Accept current conflict: Both",
            "Accept all Ours",
            "Accept all Theirs",
        });
    }

    [Fact]
    public void BuildFor_IncludesFinishingCommands()
    {
        var vm = CreateVm();
        var items = MergeCommandCatalog.BuildFor(vm);

        items.Select(i => i.DisplayName).Should().Contain(new[]
        {
            "Mark resolved",
            "Complete merge",
            "Abort merge",
        });
    }

    [Fact]
    public void EveryItem_HasICommandInTag()
    {
        var vm = CreateVm();
        var items = MergeCommandCatalog.BuildFor(vm);

        items.Should().OnlyContain(i => i.Tag is ICommand,
            because: "the palette invokes Tag as ICommand on confirm; a non-ICommand Tag would crash");
    }

    [Fact]
    public void EveryItem_HasNonEmptyDisplayName()
    {
        var vm = CreateVm();
        var items = MergeCommandCatalog.BuildFor(vm);

        items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.DisplayName));
    }

    [Fact]
    public void KeybindingsMatchExpectedAccelerators()
    {
        var vm = CreateVm();
        var items = MergeCommandCatalog.BuildFor(vm);

        items.First(i => i.DisplayName == "Next conflict").Detail.Should().Be("F8");
        items.First(i => i.DisplayName == "Previous conflict").Detail.Should().Be("Shift+F8");
        items.First(i => i.DisplayName == "Next change span").Detail.Should().Be("Alt+Right");
        items.First(i => i.DisplayName == "Previous change span").Detail.Should().Be("Alt+Left");
        items.First(i => i.DisplayName == "Mark resolved").Detail.Should().Be("Ctrl+Enter");
    }

    /// <summary>
    /// Reflection drift-guard. Enumerates every public <c>ICommand</c>
    /// property on <see cref="MergeEditorViewModel"/> and asserts each
    /// parameterless, user-invokable one has an entry in the catalog (by
    /// reference-equality on <see cref="CommandPaletteItem.Tag"/>). Adding
    /// a new <c>[RelayCommand]</c> without updating the catalog will now
    /// fail this test — the catalog's class-doc promises enumeration of
    /// "every user-invokable action" and this is the enforcement.
    /// </summary>
    [Fact]
    public void CatalogCovers_AllParameterlessUserInvokableCommands()
    {
        var vm = CreateVm();
        var items = MergeCommandCatalog.BuildFor(vm);
        var cataloged = new HashSet<ICommand>(items.Select(i => (ICommand)i.Tag!));

        // Commands explicitly excluded from the palette:
        //  • OpenPaletteCommand — the palette cannot list itself as a target
        //  • Commands that take a range-index or ConflictInfo parameter
        //    (they're invoked from the overlay UI / file list, not the
        //    palette); the palette VM has no way to supply the parameter.
        //  • RequestAiResolutionForRangeCommand — per-range AI invoke, same
        //    reasoning as the accept-ours/theirs/both per-range commands.
        //  • AcceptBothTheirsFirstCommand — advanced variant exposed via
        //    context menu only; plain "Accept Both" is the palette entry.
        var excluded = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(vm.OpenPaletteCommand),
            nameof(vm.AcceptOursCommand),
            nameof(vm.AcceptTheirsCommand),
            nameof(vm.AcceptBothCommand),
            nameof(vm.AcceptBothTheirsFirstCommand),
            nameof(vm.UnresolveCommand),
            nameof(vm.UnresolveConflictCommand),
            nameof(vm.CompareConflictCommand),
            nameof(vm.RequestAiResolutionForRangeCommand),
        };

        var commandProps = typeof(MergeEditorViewModel)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .Where(p => p.Name.EndsWith("Command", StringComparison.Ordinal))
            .ToList();

        commandProps.Should().NotBeEmpty();

        var missing = new List<string>();
        foreach (var prop in commandProps)
        {
            if (excluded.Contains(prop.Name)) continue;
            var cmd = (ICommand?)prop.GetValue(vm);
            cmd.Should().NotBeNull($"{prop.Name} must be constructed by the VM");
            if (!cataloged.Contains(cmd!))
            {
                missing.Add(prop.Name);
            }
        }

        missing.Should().BeEmpty(
            because: "every public, parameterless, user-invokable command must appear in the palette catalog — " +
                     "if you intentionally want to exclude a new command, add it to the test's allow-list");
    }

    private static MergeEditorViewModel CreateVm()
    {
        return new MergeEditorViewModel(
            new FakeGitService(),
            new FakeClipboardService(),
            new FakeMergeEngine(),
            "C:/test");
    }

    private sealed class FakeMergeEngine : IMergeEngine
    {
        public Task<MergeDocument> MergeAsync(
            string filePath, string baseText, string oursText, string theirsText,
            bool ignoreWhitespace = false, string? oursLabel = null, string? theirsLabel = null,
            string? baseLabel = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new MergeDocument(
                filePath, baseText, oursText, theirsText, "",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<ModifiedBaseRange>(), "\n", true));
    }
}

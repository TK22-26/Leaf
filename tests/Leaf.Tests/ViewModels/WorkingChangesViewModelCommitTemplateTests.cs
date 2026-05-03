using FluentAssertions;
using Leaf.ViewModels;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Pure-logic tests for the §5.15 partial — subject/body splitter and
/// the <see cref="WorkingChangesViewModel.CommitTemplateApplyMode"/> shape.
/// The full apply pipeline needs a fully-wired VM; these are the static
/// helpers we can test in isolation.
/// </summary>
public class WorkingChangesViewModelCommitTemplateTests
{
    [Fact]
    public void SplitSubjectAndBody_BlankLineIsThePreferredSeparator()
    {
        var (subj, body) = WorkingChangesViewModel.SplitSubjectAndBody("subject\n\nbody line 1\nbody line 2");
        subj.Should().Be("subject");
        body.Should().Be("body line 1\nbody line 2");
    }

    [Fact]
    public void SplitSubjectAndBody_FallsBackToFirstNewline()
    {
        var (subj, body) = WorkingChangesViewModel.SplitSubjectAndBody("subject\nbody no blank");
        subj.Should().Be("subject");
        body.Should().Be("body no blank");
    }

    [Fact]
    public void SplitSubjectAndBody_SingleLineHasEmptyBody()
    {
        var (subj, body) = WorkingChangesViewModel.SplitSubjectAndBody("only a subject");
        subj.Should().Be("only a subject");
        body.Should().Be(string.Empty);
    }

    [Fact]
    public void SplitSubjectAndBody_TrimsTrailingCrFromSubject()
    {
        var (subj, body) = WorkingChangesViewModel.SplitSubjectAndBody("subject\r\n\r\nbody");
        subj.Should().Be("subject");
        body.Should().Be("body");
    }

    [Fact]
    public void SplitSubjectAndBody_EmptyInputReturnsEmpties()
    {
        var (subj, body) = WorkingChangesViewModel.SplitSubjectAndBody(string.Empty);
        subj.Should().Be(string.Empty);
        body.Should().Be(string.Empty);
    }
}

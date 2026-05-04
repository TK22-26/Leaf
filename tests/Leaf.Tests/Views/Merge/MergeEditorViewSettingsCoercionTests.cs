#nullable enable
using FluentAssertions;
using Leaf.Views.Merge;
using Xunit;

namespace Leaf.Tests.Views.Merge;

/// <summary>
/// Pins <see cref="MergeEditorView.TryCoerceWidth"/> so a corrupt settings
/// file (NaN, Infinity, negative, absurdly large, zero) can't reach the
/// <c>new GridLength(...)</c> constructor — which throws
/// <c>ArgumentException</c> on <c>PositiveInfinity</c> and silently accepts
/// <c>NaN</c> which then poisons every subsequent measure pass.
/// </summary>
public class MergeEditorViewSettingsCoercionTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void TryCoerceWidth_RejectsNonFiniteOrNonPositive(double raw)
    {
        MergeEditorView.TryCoerceWidth(raw, 0.1, 10.0, out var clamped).Should().BeFalse();
        clamped.Should().Be(0);
    }

    [Fact]
    public void TryCoerceWidth_ClampsValueBelowMin_ToMin()
    {
        MergeEditorView.TryCoerceWidth(0.05, 0.1, 10.0, out var clamped).Should().BeTrue();
        clamped.Should().BeApproximately(0.1, precision: 1e-9);
    }

    [Fact]
    public void TryCoerceWidth_ClampsValueAboveMax_ToMax()
    {
        MergeEditorView.TryCoerceWidth(1_000_000.0, 0.1, 10.0, out var clamped).Should().BeTrue();
        clamped.Should().BeApproximately(10.0, precision: 1e-9);
    }

    [Fact]
    public void TryCoerceWidth_PassesThroughValueInsideRange()
    {
        MergeEditorView.TryCoerceWidth(2.5, 0.1, 10.0, out var clamped).Should().BeTrue();
        clamped.Should().Be(2.5);
    }

    [Fact]
    public void TryCoerceWidth_BoundsConstants_AreSensible()
    {
        // Guard against a future edit that would make the file list too
        // narrow to see filenames or pane ratios so skewed that a reload
        // surprises the user.
        MergeEditorView.MinFileListWidthPx.Should().BeInRange(20, 100);
        MergeEditorView.MaxFileListWidthPx.Should().BeGreaterThan(MergeEditorView.MinFileListWidthPx);
        MergeEditorView.MinPaneRatio.Should().BeInRange(0.05, 0.5);
        MergeEditorView.MaxPaneRatio.Should().BeGreaterThan(MergeEditorView.MinPaneRatio);
    }
}

#nullable enable
using System.Windows;
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Tests for <see cref="ImageViewportState"/>'s clamp invariants. Plain
/// data-holder with INotifyPropertyChanged — the only behaviour worth
/// testing is that zoom stays inside <c>[0.05, 32]</c>, SwipeRatio and
/// OnionSkinOpacity stay inside <c>[0, 1]</c>, and PropertyChanged fires
/// when a real change happens.
/// </summary>
public class ImageViewportStateTests
{
    [Fact]
    public void Zoom_ClampsBelowMinimum()
    {
        var vp = new ImageViewportState();
        vp.Zoom = 0.001;
        vp.Zoom.Should().Be(0.05);
    }

    [Fact]
    public void Zoom_ClampsAboveMaximum()
    {
        var vp = new ImageViewportState();
        vp.Zoom = 1000;
        vp.Zoom.Should().Be(32.0);
    }

    [Fact]
    public void SwipeRatio_ClampsToUnitInterval()
    {
        var vp = new ImageViewportState();
        vp.SwipeRatio = -1;
        vp.SwipeRatio.Should().Be(0);
        vp.SwipeRatio = 2;
        vp.SwipeRatio.Should().Be(1);
    }

    [Fact]
    public void OnionSkinOpacity_ClampsToUnitInterval()
    {
        var vp = new ImageViewportState();
        vp.OnionSkinOpacity = -0.5;
        vp.OnionSkinOpacity.Should().Be(0);
        vp.OnionSkinOpacity = 1.5;
        vp.OnionSkinOpacity.Should().Be(1);
    }

    [Fact]
    public void PropertyChanged_FiresOnRealChange()
    {
        var vp = new ImageViewportState();
        var fires = new List<string?>();
        vp.PropertyChanged += (_, e) => fires.Add(e.PropertyName);
        vp.Zoom = 2.0;
        vp.Mode = ImageMergeMode.OnionSkin;
        fires.Should().Contain(nameof(ImageViewportState.Zoom));
        fires.Should().Contain(nameof(ImageViewportState.Mode));
    }

    [Fact]
    public void PropertyChanged_DoesNotFireOnNoOp()
    {
        var vp = new ImageViewportState { Zoom = 2.0 };
        var fires = new List<string?>();
        vp.PropertyChanged += (_, e) => fires.Add(e.PropertyName);
        vp.Zoom = 2.0; // same value
        fires.Should().BeEmpty();
    }

    [Fact]
    public void Pan_IsPersisted()
    {
        var vp = new ImageViewportState();
        vp.Pan = new Point(10, 20);
        vp.Pan.Should().Be(new Point(10, 20));
    }
}

#nullable enable
using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.Tests.Fakes;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Verifies that <see cref="NotificationService"/> consults
/// <see cref="AppSettings"/> when a category is supplied. A null
/// category bypasses the filter — that's the always-show path used by
/// error toasts and pre-existing direct callers.
/// </summary>
public class NotificationServiceFilterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly FakeDispatcherService _dispatcher;
    private readonly NotificationService _service;
    private int _firedCount;

    public NotificationServiceFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"leaf-notify-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settings = new SettingsService(_tempDir);
        _dispatcher = new FakeDispatcherService();
        _service = new NotificationService(_dispatcher, _settings);
        _service.NotificationRequested += _ => Interlocked.Increment(ref _firedCount);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void NullCategory_AlwaysFires()
    {
        // Even when every category is off, a null-category call (the
        // error path) still surfaces. This is the contract that keeps
        // user-muted preferences from hiding failures.
        var s = _settings.LoadSettings();
        s.NotifySyncOperations = false;
        s.NotifyMergeAndRebase = false;
        _settings.SaveSettings(s);

        _service.Show("Hello", "World", NotificationType.Error, category: null);

        _firedCount.Should().Be(1);
    }

    [Fact]
    public void CategoryOn_Fires()
    {
        var s = _settings.LoadSettings();
        s.NotifySyncOperations = true;
        _settings.SaveSettings(s);

        _service.Show("Pull complete", "Up to date.", NotificationType.Success, NotificationCategory.SyncOperations);

        _firedCount.Should().Be(1);
    }

    [Fact]
    public void CategoryOff_Skips()
    {
        var s = _settings.LoadSettings();
        s.NotifySyncOperations = false;
        _settings.SaveSettings(s);

        _service.Show("Pull complete", "Up to date.", NotificationType.Success, NotificationCategory.SyncOperations);

        _firedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(NotificationCategory.BranchAdmin)]
    [InlineData(NotificationCategory.Stash)]
    [InlineData(NotificationCategory.RemoteConfig)]
    [InlineData(NotificationCategory.CancelledOperations)]
    public void DefaultOff_CategoriesSkipOnFreshInstall(NotificationCategory category)
    {
        // Fresh settings (no prior write) — the categories we elected to
        // mute by default must not fire. Catches a regression where
        // someone changes a default to true.
        _service.Show("Anything", "Anything", NotificationType.Success, category);

        _firedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(NotificationCategory.SyncOperations)]
    [InlineData(NotificationCategory.MergeAndRebase)]
    [InlineData(NotificationCategory.BranchCheckout)]
    [InlineData(NotificationCategory.GitFlow)]
    [InlineData(NotificationCategory.Worktree)]
    [InlineData(NotificationCategory.Submodule)]
    [InlineData(NotificationCategory.PullRequest)]
    [InlineData(NotificationCategory.Patch)]
    [InlineData(NotificationCategory.Repository)]
    public void DefaultOn_CategoriesFireOnFreshInstall(NotificationCategory category)
    {
        // The high-value categories must fire by default so a fresh
        // install still gets push/pull/merge/checkout confirmations.
        _service.Show("Anything", "Anything", NotificationType.Success, category);

        _firedCount.Should().Be(1);
    }
}

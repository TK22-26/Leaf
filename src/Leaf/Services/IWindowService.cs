using System.Windows;

namespace Leaf.Services;

/// <summary>
/// Abstraction for window owner access.
/// Prevents services from depending on Application.Current.MainWindow directly.
/// </summary>
public interface IWindowService
{
    /// <summary>
    /// Gets the main application window, or null if not available.
    /// </summary>
    /// <returns>The main window, or null during startup/shutdown or in tests.</returns>
    Window? GetMainWindow();
}

/// <summary>
/// WPF implementation of <see cref="IWindowService"/>.
/// Safely accesses Application.Current.MainWindow.
/// </summary>
public class WindowService : IWindowService
{
    /// <inheritdoc />
    public Window? GetMainWindow()
    {
        // Safe access - returns null if not available
        return Application.Current?.MainWindow;
    }
}

/// <summary>
/// Test implementation of <see cref="IWindowService"/>.
/// Returns null (no owner window in tests).
/// </summary>
public class TestWindowService : IWindowService
{
    /// <inheritdoc />
    public Window? GetMainWindow() => null;
}

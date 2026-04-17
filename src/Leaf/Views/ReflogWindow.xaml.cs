using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Host window for the reflog view. Responsibilities:
/// <list type="bullet">
///   <item>Kick off <c>LoadAsync</c> once WPF has laid the window out.</item>
///   <item>Debounce the search box so every keystroke doesn't re-filter
///         thousands of entries; only push the value to the view-model
///         after the user pauses.</item>
///   <item>Copy the full SHA when the user clicks a SHA cell's text.</item>
/// </list>
/// </summary>
public partial class ReflogWindow : Window
{
    private readonly ReflogViewModel _viewModel;
    private readonly DispatcherTimer _searchDebounceTimer;

    public ReflogWindow(ReflogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 250 ms matches the cadence Leaf uses for the commit search box
        // — long enough that a typical typing speed (~5 chars/second)
        // triggers one re-filter per burst, short enough that the user
        // still feels the list responding to them.
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _searchDebounceTimer.Tick += SearchDebounce_Tick;

        Loaded += Window_Loaded;
        Closed += Window_Closed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Tick -= SearchDebounce_Tick;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Each keystroke restarts the debounce timer. Only when the user
    /// pauses (the timer actually fires) do we push the text to the
    /// view-model, which runs the filter. Avoids a re-filter on every
    /// keypress when the user is still typing.
    /// </summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchDebounce_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        _viewModel.SearchText = SearchBox.Text;
    }

    /// <summary>
    /// Clicking a SHA cell copies the full 40-char hash to the
    /// clipboard. The TextBlock's Tag carries the full SHA so the
    /// handler doesn't have to dig through <c>DataContext</c>.
    /// Deliberately not marking the event handled — row selection
    /// still propagates through the DataGrid.
    /// </summary>
    private void ShaCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock tb && tb.Tag is string fullSha)
        {
            _viewModel.CopyShaToClipboard(fullSha);
        }
    }
}

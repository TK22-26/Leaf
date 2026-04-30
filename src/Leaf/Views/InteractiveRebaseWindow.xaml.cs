using System.Windows;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Code-behind for the interactive-rebase editor. Stays as small as the
/// other power-user windows (ReflogWindow, MergeEditorView): wires the
/// view-model's terminal events to <see cref="Window.Close()"/>, nothing
/// else. The plan, commands, and validation all live on
/// <see cref="InteractiveRebaseViewModel"/>.
/// </summary>
public partial class InteractiveRebaseWindow : Window
{
    public InteractiveRebaseWindow(InteractiveRebaseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        viewModel.RebaseCompleted += OnRebaseCompleted;
        viewModel.Cancelled += OnCancelled;
        Closed += (_, _) =>
        {
            viewModel.RebaseCompleted -= OnRebaseCompleted;
            viewModel.Cancelled -= OnCancelled;
        };
    }

    private void OnRebaseCompleted(object? sender, Models.MergeResult result)
    {
        // Result.HasConflicts means the rebase paused on a conflict; the
        // host (MainViewModel) is the one that opens the merge editor and
        // refreshes the repo. We just close the window so the merge editor
        // can take focus without overlapping us.
        if (IsLoaded) Close();
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        if (IsLoaded) Close();
    }
}

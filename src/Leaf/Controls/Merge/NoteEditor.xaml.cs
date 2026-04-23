#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Leaf.Controls.Merge;

/// <summary>
/// Inline textarea overlay for attaching a user-authored note to a
/// conflict range (C6). Presentation-only: host owns the Popup and
/// forwards <see cref="CommitRequested"/> to
/// <c>MergeEditorViewModel.AddNoteCommand</c>.
/// </summary>
/// <remarks>
/// Keyboard contract:
/// <list type="bullet">
///   <item><description>Ctrl+Enter — commit (supports multi-line notes).</description></item>
///   <item><description>Escape — cancel.</description></item>
///   <item><description>Save button — also commits; IsDefault=true so single-line
///   flows can just press Enter on the button after typing.</description></item>
/// </list>
/// Plain <c>Enter</c> inside the TextBox inserts a newline because
/// AcceptsReturn is true; this matches the "notes may be multi-paragraph"
/// contract the plan §C6 describes for conflict explanations.
/// </remarks>
public partial class NoteEditor : UserControl
{
    public string NoteText
    {
        get => NoteInput.Text;
        set => NoteInput.Text = value ?? string.Empty;
    }

    /// <summary>Fires when the user presses Save or Ctrl+Enter. Arg = trimmed note text (empty-string means "clear").</summary>
    public event EventHandler<string>? CommitRequested;

    /// <summary>Fires when the user presses Escape or Cancel.</summary>
    public event EventHandler? CancelRequested;

    public NoteEditor()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NoteInput.Focus();
            NoteInput.CaretIndex = NoteInput.Text.Length;
        };
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CommitCurrent();
            e.Handled = true;
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e) => CommitCurrent();

    private void OnCancelClicked(object sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void CommitCurrent() =>
        CommitRequested?.Invoke(this, NoteInput.Text?.Trim() ?? string.Empty);
}

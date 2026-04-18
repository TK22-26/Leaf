#nullable enable
using System.Windows;

namespace Leaf.Views.Merge;

/// <summary>
/// First-run consent dialog for the AI merge assistant (plan Phase 5). Shows
/// the configured MCP server path + a summary of what data leaves Leaf, and
/// lets the user accept once-per-install (or cancel for this session).
/// </summary>
/// <remarks>
/// The dialog is deliberately "dumb": the <see cref="MergeEditorViewModel.Ai"/>
/// partial fires a consent-request event with the server path + context-line
/// count, the host view constructs the dialog with those values, and the user's
/// accept/cancel decision is the dialog's only output. Writing the acknowledged
/// flag to <see cref="Services.SettingsService"/> is the host view's responsibility
/// — keeps the dialog testable and coupling-free.
/// </remarks>
public partial class AiConsentDialog : Window
{
    public AiConsentDialog(string mcpServerPath, int contextLines)
    {
        InitializeComponent();
        ServerPathText.Text = string.IsNullOrWhiteSpace(mcpServerPath)
            ? "(no server configured — set one in Settings → AI Merge)"
            : mcpServerPath;
        ContextLinesText.Text = $"Context window: {contextLines} lines above + {contextLines} lines below.";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

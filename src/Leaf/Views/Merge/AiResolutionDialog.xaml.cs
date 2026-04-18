#nullable enable
using System.Windows;
using System.Windows.Media;
using Leaf.Services.Merge;

namespace Leaf.Views.Merge;

/// <summary>
/// Preview + edit dialog for an AI-proposed conflict resolution. The proposed
/// text is shown editable; whatever the user has on accept becomes the
/// <see cref="Leaf.Models.Merge.ResolutionState.Manual"/> value applied to
/// that conflict range.
/// </summary>
/// <remarks>
/// Treating the AI output as a starting draft (not a "trust-me-bro" button)
/// is the point of the preview — matches the plan's safety position that the
/// user stays in the driver's seat.
/// </remarks>
public partial class AiResolutionDialog : Window
{
    public AiResolutionDialog(string proposedText, string rationale, AiConfidence confidence)
    {
        InitializeComponent();
        ProposedTextBox.Text = proposedText ?? string.Empty;
        RationaleText.Text = string.IsNullOrWhiteSpace(rationale)
            ? "(no rationale provided)"
            : rationale;

        ConfidenceText.Text = confidence.ToString().ToUpperInvariant() + " CONFIDENCE";
        ConfidenceChip.Background = ConfidenceToBrush(confidence);
    }

    /// <summary>Text the user has in the editor on Accept.</summary>
    public string AcceptedText { get; private set; } = string.Empty;

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        AcceptedText = ProposedTextBox.Text ?? string.Empty;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static Brush ConfidenceToBrush(AiConfidence confidence) => confidence switch
    {
        AiConfidence.High => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),   // green
        AiConfidence.Low => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),    // red
        _ => new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00)),                   // amber (medium)
    };
}

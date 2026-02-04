using Leaf.Models;

namespace Leaf.Controls.GitGraph;

/// <summary>
/// Event args for requesting a branch checkout from the graph.
/// </summary>
public sealed class BranchCheckoutRequestedEventArgs : EventArgs
{
    public BranchCheckoutRequestedEventArgs(BranchLabel label, string? tipSha)
    {
        Label = label;
        TipSha = tipSha;
    }

    public BranchLabel Label { get; }

    public string? TipSha { get; }
}

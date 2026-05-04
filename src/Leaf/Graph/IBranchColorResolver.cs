using System.Windows.Media;

namespace Leaf.Graph;

/// <summary>
/// Resolves the display colour for a branch label. Introduced so views can ask
/// for a branch colour without reaching into <see cref="GraphBuilder"/>
/// statically — the concrete resolver is owned per-repo by the active view
/// model, keeping colour state scoped to the repository being shown.
/// </summary>
public interface IBranchColorResolver
{
    /// <summary>
    /// Returns the brush to use for the given branch name. Implementations
    /// return a cached <see cref="SolidColorBrush"/> that is already frozen.
    /// </summary>
    Brush GetBranchColor(string branchName);
}

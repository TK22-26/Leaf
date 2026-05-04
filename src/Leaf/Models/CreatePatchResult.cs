namespace Leaf.Models;

/// <summary>
/// What <c>git format-patch</c> produced. <see cref="Files"/> is the
/// ordered list of <c>.patch</c> files in <see cref="OutputDirectory"/>;
/// <see cref="ErrorMessage"/> is non-empty when git refused to run.
/// </summary>
public sealed class CreatePatchResult
{
    public bool Success { get; init; }
    public string OutputDirectory { get; init; } = string.Empty;
    public IReadOnlyList<string> Files { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

using System.Text;
using Leaf.Models;

namespace Leaf.Services.Merge;

/// <summary>
/// Composes merged content from resolution choices.
/// </summary>
public sealed class ConflictComposer : IConflictComposer
{
    public string ComposeMergedContent(FileMergeResult result)
    {
        var sb = new StringBuilder();
        var first = true;

        foreach (var region in result.Regions)
        {
            var content = region.GetResolvedContent();
            if (string.IsNullOrEmpty(content) && region.IsConflict && !region.IsResolved)
                continue;

            if (!first && !string.IsNullOrEmpty(content))
                sb.Append('\n');

            sb.Append(content);
            first = false;
        }

        return sb.ToString();
    }

    public void ApplyManualEdit(FileMergeResult result, int regionIndex, string editedContent)
    {
        if (regionIndex < 0 || regionIndex >= result.Regions.Count)
            return;

        var region = result.Regions[regionIndex];
        if (!region.IsConflict)
            return;

        region.ManualEditContent = editedContent;
        region.IsManualEditMode = true;
        region.Resolution = ConflictResolution.UseManual;
    }
}

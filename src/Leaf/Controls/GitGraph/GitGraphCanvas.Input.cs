using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Controls.GitGraph.Services;
using Leaf.Graph;
using Leaf.Models;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    private System.Windows.Threading.DispatcherTimer? _animationTimer;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Handle double-click on expanded branch item
        if (e.ClickCount == 2)
        {
            var pos = e.GetPosition(this);
            var expandedHitAreas = _hitTestService.GetAllExpandedItemHitAreas();

            foreach (var kvp in expandedHitAreas)
            {
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    var item = kvp.Value[i];
                    if (item.HitArea.Contains(pos))
                    {
                        string? tipSha = null;
                        if (Nodes != null && kvp.Key >= 0 && kvp.Key < Nodes.Count)
                        {
                            tipSha = Nodes[kvp.Key].Sha;
                        }

                        // Double-clicked on a branch - request checkout
                        BranchCheckoutRequested?.Invoke(
                            this,
                            new BranchCheckoutRequestedEventArgs(item.Label, tipSha));
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
    }

    private void StartExpansionAnimation(int nodeIndex, bool expanding)
    {
        if (_animationTimer == null)
        {
            _animationTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AnimationStep)
            };
            _animationTimer.Tick += OnAnimationTick;
        }

        // Set initial progress
        if (expanding)
        {
            _stateService.SetExpansionProgress(nodeIndex, 0.0);
        }
        // For collapsing, progress will decrease from current value

        if (!_animationTimer.IsEnabled)
        {
            _animationTimer.Start();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        _stateService.UpdateAnimationProgress(AnimationStep, AnimationDuration);
        InvalidateVisual();

        if (!_stateService.HasActiveAnimations())
        {
            _animationTimer?.Stop();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);

        // Check if clicking on any overflow indicator
        if (pos.X < LabelAreaWidth)
        {
            // Check branch overflow areas
            var overflowAreas = _hitTestService.GetAllOverflowAreas();
            foreach (var kvp in overflowAreas)
            {
                int displayRow = kvp.Key;
                var overflow = kvp.Value;

                if (overflow.HitArea.Contains(pos))
                {
                    int rowOffset = (HasWorkingChanges ? 1 : 0) + StashCount;
                    int nodeIndex = displayRow - rowOffset;

                    if (nodeIndex >= 0)
                    {
                        bool wasExpanded = _stateService.IsNodeExpanded(nodeIndex);
                        bool isNowExpanded = _stateService.ToggleNodeExpansion(nodeIndex, overflow.Labels.Count);

                        if (!isNowExpanded)
                        {
                            _hitTestService.ClearExpandedItemHitAreas(nodeIndex);
                            if (!_stateService.HasExpandedNodes())
                                Mouse.Capture(null);
                        }
                        else
                        {
                            Mouse.Capture(this, CaptureMode.SubTree);
                        }

                        StartExpansionAnimation(nodeIndex, isNowExpanded);
                        InvalidateVisual();
                        InvalidateMeasure();

                        // Notify listeners
                        if (_stateService is GitGraphStateService stateImpl)
                        {
                            stateImpl.RaiseRowExpansionChanged(
                                nodeIndex,
                                isNowExpanded,
                                isNowExpanded ? overflow.Labels.Count : 0,
                                TotalExpansionHeight);
                        }
                    }
                    e.Handled = true;
                    return;
                }
            }

            // Check tag overflow areas
            var tagOverflowAreas = _hitTestService.GetAllTagOverflowAreas();
            foreach (var kvp in tagOverflowAreas)
            {
                int displayRow = kvp.Key;
                var overflow = kvp.Value;

                if (overflow.HitArea.Contains(pos))
                {
                    int rowOffset = (HasWorkingChanges ? 1 : 0) + StashCount;
                    int nodeIndex = displayRow - rowOffset;

                    if (nodeIndex >= 0)
                    {
                        bool wasExpanded = _stateService.IsTagNodeExpanded(nodeIndex);
                        bool isNowExpanded = _stateService.ToggleTagNodeExpansion(nodeIndex);

                        if (!isNowExpanded)
                        {
                            _hitTestService.ClearExpandedTagHitAreas(nodeIndex);
                            if (!_stateService.HasExpandedNodes())
                                Mouse.Capture(null);
                        }
                        else
                        {
                            Mouse.Capture(this, CaptureMode.SubTree);
                        }

                        InvalidateVisual();
                        InvalidateMeasure();
                    }
                    e.Handled = true;
                    return;
                }
            }
        }

        // If clicking outside any expanded area, collapse all
        if (_stateService.HasExpandedNodes())
        {
            bool clickedInsideExpanded = _hitTestService.IsInsideExpandedArea(pos);

            if (!clickedInsideExpanded)
            {
                CollapseAllExpandedTags();
                CollapseAllExpandedTagLabels();
            }
        }
    }

    private void CollapseAllExpandedTags()
    {
        var expandedNodes = _stateService.GetExpandedNodes();
        if (expandedNodes.Count == 0)
            return;

        var nodesToCollapse = expandedNodes.Keys.ToList();
        foreach (var nodeIndex in nodesToCollapse)
        {
            _hitTestService.ClearExpandedItemHitAreas(nodeIndex);
            StartExpansionAnimation(nodeIndex, false);
        }

        _stateService.CollapseAllNodes();
        Mouse.Capture(null);

        InvalidateVisual();
        InvalidateMeasure();

        if (_stateService is GitGraphStateService stateImpl)
        {
            stateImpl.RaiseRowExpansionChanged(-1, false, 0, TotalExpansionHeight);
        }
    }

    private void CollapseAllExpandedTagLabels()
    {
        var expandedTagNodes = _stateService.GetExpandedTagNodes();
        if (expandedTagNodes.Count == 0)
            return;

        foreach (var nodeIndex in expandedTagNodes.ToList())
        {
            _hitTestService.ClearExpandedTagHitAreas(nodeIndex);
        }

        _stateService.CollapseAllTagNodes();

        if (!_stateService.HasExpandedNodes())
        {
            Mouse.Capture(null);
        }

        InvalidateVisual();
        InvalidateMeasure();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var nodes = Nodes;

        // Check for hover over expanded dropdown items first
        bool foundExpandedHover = false;
        var expandedHitAreas = _hitTestService.GetAllExpandedItemHitAreas();

        foreach (var kvp in expandedHitAreas)
        {
            for (int i = 0; i < kvp.Value.Count; i++)
            {
                if (kvp.Value[i].HitArea.Contains(pos))
                {
                    var currentHovered = _stateService.HoveredExpandedItem;
                    if (currentHovered != (kvp.Key, i))
                    {
                        _stateService.HoveredExpandedItem = (kvp.Key, i);
                        Cursor = Cursors.Hand;
                        InvalidateVisual();
                    }
                    foundExpandedHover = true;
                    break;
                }
            }
            if (foundExpandedHover) break;
        }

        if (!foundExpandedHover && _stateService.HoveredExpandedItem.NodeIndex >= 0)
        {
            _stateService.HoveredExpandedItem = (-1, -1);
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }

        // Check for overflow indicator hover first - show tooltip
        if (pos.X < LabelAreaWidth)
        {
            var overflowAreas = _hitTestService.GetAllOverflowAreas();
            foreach (var kvp in overflowAreas)
            {
                int displayRow = kvp.Key;
                var overflow = kvp.Value;

                if (overflow.HitArea.Contains(pos))
                {
                    if (_stateService.HoveredOverflowRow != displayRow)
                    {
                        _stateService.HoveredOverflowRow = displayRow;
                        // Show popup with all branch names
                        int tooltipRowOffset = (HasWorkingChanges ? 1 : 0) + StashCount;
                        int tooltipNodeIndex = displayRow - tooltipRowOffset;
                        if (Nodes != null && tooltipNodeIndex >= 0 && tooltipNodeIndex < Nodes.Count)
                        {
                            var tooltipNode = Nodes[tooltipNodeIndex];
                            ShowBranchTooltip(tooltipNode.BranchLabels, overflow.HitArea);
                        }
                    }
                    Cursor = Cursors.Hand;
                    return;
                }
            }

            var tagOverflowAreas = _hitTestService.GetAllTagOverflowAreas();
            foreach (var kvp in tagOverflowAreas)
            {
                if (kvp.Value.HitArea.Contains(pos))
                {
                    Cursor = Cursors.Hand;
                    return;
                }
            }
        }

        // If we were hovering over overflow but now left, hide tooltip
        if (_stateService.HoveredOverflowRow >= 0)
        {
            _stateService.HoveredOverflowRow = -1;
            HideBranchTooltip();
            Cursor = Cursors.Arrow;
        }

        // Calculate which row the mouse is over
        int row = (int)(pos.Y / RowHeight);
        int currentRow = 0;

        // Handle working changes row
        if (HasWorkingChanges)
        {
            if (row == currentRow)
            {
                IsWorkingChangesHovered = true;
                HoveredSha = null;
                HoveredStashIndex = -1;
                return;
            }
            currentRow++;
            IsWorkingChangesHovered = false;
        }
        else
        {
            IsWorkingChangesHovered = false;
        }

        // Handle stash rows
        if (StashCount > 0)
        {
            int stashIndex = row - currentRow;
            if (stashIndex >= 0 && stashIndex < StashCount)
            {
                HoveredStashIndex = stashIndex;
                HoveredSha = null;
                return;
            }
            HoveredStashIndex = -1;
            currentRow += StashCount;
        }
        else
        {
            HoveredStashIndex = -1;
        }

        if (nodes == null || nodes.Count == 0)
        {
            HoveredSha = null;
            return;
        }

        int nodeIndex = row - currentRow;
        if (nodeIndex >= 0 && nodeIndex < nodes.Count)
        {
            HoveredSha = nodes[nodeIndex].Sha;
        }
        else
        {
            HoveredSha = null;
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        HoveredSha = null;
        IsWorkingChangesHovered = false;
        HoveredStashIndex = -1;

        if (_stateService.HoveredOverflowRow >= 0)
        {
            _stateService.HoveredOverflowRow = -1;
            HideBranchTooltip();
        }
    }

    public BranchLabel? GetBranchLabelAt(Point position)
    {
        if (Nodes == null || position.X > LabelAreaWidth)
            return null;

        int row = (int)(position.Y / RowHeight);
        int rowOffset = (HasWorkingChanges ? 1 : 0) + StashCount;
        int nodeIndex = row - rowOffset;

        if (nodeIndex < 0 || nodeIndex >= Nodes.Count)
            return null;

        var node = Nodes[nodeIndex];
        if (node.BranchLabels.Count == 0)
            return null;

        double y = GetYForRow(node.RowIndex + rowOffset);
        double labelX = 4;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int drawnCount = 0;

        foreach (var label in node.BranchLabels)
        {
            double fontSize = label.IsCurrent ? 13 : 11;
            double iconFontSize = label.IsCurrent ? 13 : 11;
            double labelHeight = label.IsCurrent ? 22 : 18;

            // Build icon text matching rendering: local icon + generic cloud remotes
            var iconText = "";
            if (label.IsLocal)
                iconText += ComputerIcon;

            int customIconCount = 0;
            int genericCloudCount = 0;
            foreach (var remote in label.Remotes)
            {
                if (remote.RemoteType == RemoteType.GitHub || remote.RemoteType == RemoteType.AzureDevOps)
                    customIconCount++;
                else
                    genericCloudCount++;
            }

            if (label.IsLocal && (customIconCount > 0 || genericCloudCount > 0))
                iconText += " ";
            for (int i = 0; i < genericCloudCount; i++)
            {
                if (i > 0) iconText += " ";
                iconText += CloudIcon;
            }

            // Use IconTypeface for icon measurement (must match rendering)
            var iconFormatted = new FormattedText(
                iconText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                IconTypeface,
                iconFontSize,
                LabelTextBrush,
                dpi);

            var nameFormatted = new FormattedText(
                label.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                fontSize,
                LabelTextBrush,
                dpi);
            nameFormatted.MaxLineCount = 1;
            nameFormatted.Trimming = TextTrimming.CharacterEllipsis;

            // Calculate custom remote icon sizes (GitHub/Azure DevOps logos)
            double singleCustomIconSize = iconFontSize;
            double customIconSpacing = 2;
            double totalCustomIconWidth = customIconCount > 0
                ? (customIconCount * singleCustomIconSize) + ((customIconCount - 1) * customIconSpacing)
                : 0;
            double customIconSpace = (customIconCount > 0 && iconFormatted.Width > 0) ? 2 : 0;

            double iconWidth = iconFormatted.Width;
            double nameWidth = nameFormatted.Width;
            double hPadding = label.IsCurrent ? 8 : 6;

            // Account for overflow suffix (matches rendering logic)
            int remainingAfterThis = node.BranchLabels.Count - drawnCount - 1;
            double suffixWidth = 0;
            if (remainingAfterThis > 0)
            {
                var suffixFormatted = new FormattedText(
                    $" +{remainingAfterThis}",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    fontSize,
                    LabelTextBrush,
                    dpi);
                suffixWidth = suffixFormatted.Width;
            }

            double iconBlockWidth = iconWidth + customIconSpace + totalCustomIconWidth;
            double gapBetweenIconAndSuffix = (suffixWidth > 0 && iconBlockWidth > 0) ? 6 : 0;
            double rightSectionWidth = iconBlockWidth + gapBetweenIconAndSuffix + suffixWidth;
            double gapBetweenNameAndRight = rightSectionWidth > 0 ? 6 : 0;

            double availableWidth = LabelAreaWidth - 8 - labelX;
            double minRequiredWidth = (hPadding * 2) + rightSectionWidth + gapBetweenNameAndRight;

            if (availableWidth <= minRequiredWidth)
                break;

            // Constrain name width to available space (matches rendering truncation)
            double nameMaxWidth = availableWidth - minRequiredWidth;
            if (nameWidth > nameMaxWidth)
                nameWidth = nameMaxWidth;

            double totalWidth = (hPadding * 2) + gapBetweenNameAndRight + rightSectionWidth + nameWidth;

            var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
            if (labelRect.Contains(position))
                return label;

            drawnCount++;

            // If overflow suffix was needed here, this is the last label
            if (remainingAfterThis > 0)
            {
                // Check if next label would actually fit (same logic as rendering)
                double nextLabelX = labelX + totalWidth + 4;
                double nextAvailable = LabelAreaWidth - 8 - nextLabelX;
                if (nextAvailable <= minRequiredWidth)
                    break;
            }

            labelX += totalWidth + 4;
        }

        return null;
    }
}

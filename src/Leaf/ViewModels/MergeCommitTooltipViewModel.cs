using System;
using System.Collections.ObjectModel;
using System.Linq;
using Leaf.Models;

namespace Leaf.ViewModels;

public class MergeCommitTooltipViewModel
{
    private const int MaxVisibleCommits = 10;

    public MergeCommitTooltipViewModel(
        ObservableCollection<CommitInfo> commits,
        ObservableCollection<GitTreeNode> nodes,
        int maxLane,
        double rowHeight)
    {
        Commits = commits;
        Nodes = nodes;
        MaxLane = maxLane;
        RowHeight = rowHeight;

        VisibleCommits = new ObservableCollection<CommitInfo>(commits.Take(MaxVisibleCommits));
        HasOverflow = commits.Count > MaxVisibleCommits;
        OverflowCount = Math.Max(0, commits.Count - MaxVisibleCommits);
        GraphHeight = VisibleCommits.Count * rowHeight;
        TotalHeight = (VisibleCommits.Count + (HasOverflow ? 1 : 0)) * rowHeight;
    }

    public ObservableCollection<CommitInfo> Commits { get; }

    public ObservableCollection<CommitInfo> VisibleCommits { get; }

    public ObservableCollection<GitTreeNode> Nodes { get; }

    public int MaxLane { get; }

    public double RowHeight { get; }

    public bool HasOverflow { get; }

    public int OverflowCount { get; }

    public double GraphHeight { get; }

    public double TotalHeight { get; }
}

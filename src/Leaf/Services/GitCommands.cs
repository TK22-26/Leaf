namespace Leaf.Services;

/// <summary>
/// Git checkout command.
/// </summary>
public class CheckoutCommand : GitCommand
{
    public string Target { get; }
    public bool Force { get; init; }
    public bool CreateBranch { get; init; }

    public CheckoutCommand(string target)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "checkout" };
        if (Force) args.Add("--force");
        if (CreateBranch) args.Add("-b");
        args.Add(Target);
        return args;
    }
}

/// <summary>
/// Git push command.
/// </summary>
public class PushCommand : GitCommand
{
    public string Remote { get; }
    public string? Branch { get; init; }
    public bool SetUpstream { get; init; }
    public bool Force { get; init; }
    public bool Delete { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }

    public PushCommand(string remote)
    {
        Remote = remote ?? throw new ArgumentNullException(nameof(remote));
    }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "push" };
        if (SetUpstream) args.Add("-u");
        if (Force) args.Add("--force");
        if (Delete) args.Add("--delete");
        args.Add(Remote);
        if (Branch != null) args.Add(Branch);
        if (Tags != null)
        {
            foreach (var tag in Tags)
            {
                args.Add(tag);
            }
        }
        return args;
    }
}

/// <summary>
/// Git pull command.
/// </summary>
public class PullCommand : GitCommand
{
    public string? Remote { get; init; }
    public string? Branch { get; init; }
    public bool Rebase { get; init; }
    public bool FastForwardOnly { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "pull" };
        if (Rebase) args.Add("--rebase");
        if (FastForwardOnly) args.Add("--ff-only");
        if (Remote != null) args.Add(Remote);
        if (Branch != null) args.Add(Branch);
        return args;
    }
}

/// <summary>
/// Git fetch command.
/// </summary>
public class FetchCommand : GitCommand
{
    public string? Remote { get; init; }
    public bool Prune { get; init; }
    public bool Tags { get; init; }
    public bool All { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "fetch" };
        if (All) args.Add("--all");
        if (Prune) args.Add("--prune");
        if (Tags) args.Add("--tags");
        if (Remote != null && !All) args.Add(Remote);
        return args;
    }
}

/// <summary>
/// Git diff command.
/// </summary>
public class DiffCommand : GitCommand
{
    public IReadOnlyList<string>? Paths { get; init; }
    public string? Commit { get; init; }
    public string? CompareCommit { get; init; }
    public bool Staged { get; init; }
    public bool NameOnly { get; init; }
    public bool NoColor { get; init; } = true;

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "diff" };
        if (NoColor) args.Add("--no-color");
        if (NameOnly) args.Add("--name-only");
        if (Staged) args.Add("--staged");
        if (Commit != null) args.Add(Commit);
        if (CompareCommit != null) args.Add(CompareCommit);
        if (Paths != null && Paths.Count > 0)
        {
            args.Add("--");
            args.AddRange(Paths);
        }
        return args;
    }
}

/// <summary>
/// Git branch command.
/// </summary>
public class BranchCommand : GitCommand
{
    public string? Name { get; init; }
    public bool Delete { get; init; }
    public bool ForceDelete { get; init; }
    public bool List { get; init; }
    public bool All { get; init; }
    public string? SetUpstreamTo { get; init; }
    public string? MoveTo { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "branch" };
        if (Delete) args.Add("-d");
        if (ForceDelete) args.Add("-D");
        if (All) args.Add("-a");
        if (MoveTo != null)
        {
            args.Add("-m");
            if (Name != null) args.Add(Name);
            args.Add(MoveTo);
            return args;
        }
        if (SetUpstreamTo != null)
        {
            args.Add("--set-upstream-to");
            args.Add(SetUpstreamTo);
        }
        if (Name != null) args.Add(Name);
        return args;
    }
}

/// <summary>
/// Git stash command.
/// </summary>
public class StashCommand : GitCommand
{
    public StashOperation Operation { get; init; } = StashOperation.Push;
    public string? Message { get; init; }
    public bool IncludeUntracked { get; init; }
    public bool KeepIndex { get; init; }
    public int? Index { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "stash" };
        switch (Operation)
        {
            case StashOperation.Push:
                args.Add("push");
                if (Message != null)
                {
                    args.Add("-m");
                    args.Add(Message);
                }
                if (IncludeUntracked) args.Add("-u");
                if (KeepIndex) args.Add("--keep-index");
                break;
            case StashOperation.Pop:
                args.Add("pop");
                if (Index.HasValue) args.Add($"stash@{{{Index.Value}}}");
                break;
            case StashOperation.Apply:
                args.Add("apply");
                if (Index.HasValue) args.Add($"stash@{{{Index.Value}}}");
                break;
            case StashOperation.Drop:
                args.Add("drop");
                if (Index.HasValue) args.Add($"stash@{{{Index.Value}}}");
                break;
            case StashOperation.List:
                args.Add("list");
                break;
            case StashOperation.Clear:
                args.Add("clear");
                break;
        }
        return args;
    }
}

/// <summary>
/// Stash operations.
/// </summary>
public enum StashOperation
{
    Push,
    Pop,
    Apply,
    Drop,
    List,
    Clear
}

/// <summary>
/// Git merge command.
/// </summary>
public class MergeCommand : GitCommand
{
    public string Branch { get; }
    public bool NoFastForward { get; init; }
    public bool FastForwardOnly { get; init; }
    public bool Squash { get; init; }
    public bool Abort { get; init; }
    public bool Continue { get; init; }
    public string? Message { get; init; }

    public MergeCommand(string branch)
    {
        Branch = branch ?? throw new ArgumentNullException(nameof(branch));
    }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "merge" };
        if (Abort)
        {
            args.Add("--abort");
            return args;
        }
        if (Continue)
        {
            args.Add("--continue");
            return args;
        }
        if (NoFastForward) args.Add("--no-ff");
        if (FastForwardOnly) args.Add("--ff-only");
        if (Squash) args.Add("--squash");
        if (Message != null)
        {
            args.Add("-m");
            args.Add(Message);
        }
        args.Add(Branch);
        return args;
    }
}

/// <summary>
/// Git rebase command.
/// </summary>
public class RebaseCommand : GitCommand
{
    public string? Onto { get; init; }
    public bool Abort { get; init; }
    public bool Continue { get; init; }
    public bool Skip { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "rebase" };
        if (Abort)
        {
            args.Add("--abort");
            return args;
        }
        if (Continue)
        {
            args.Add("--continue");
            return args;
        }
        if (Skip)
        {
            args.Add("--skip");
            return args;
        }
        if (Onto != null) args.Add(Onto);
        return args;
    }
}

/// <summary>
/// Git reset command.
/// </summary>
public class ResetCommand : GitCommand
{
    public string? Target { get; init; }
    public GitResetMode Mode { get; init; } = GitResetMode.Mixed;
    public IReadOnlyList<string>? Paths { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "reset" };
        switch (Mode)
        {
            case GitResetMode.Soft:
                args.Add("--soft");
                break;
            case GitResetMode.Hard:
                args.Add("--hard");
                break;
            case GitResetMode.Mixed:
                // Default, no flag needed
                break;
        }
        if (Target != null) args.Add(Target);
        if (Paths != null && Paths.Count > 0)
        {
            args.Add("--");
            args.AddRange(Paths);
        }
        return args;
    }
}

/// <summary>
/// Git reset modes (named GitResetMode to avoid collision with LibGit2Sharp.ResetMode).
/// </summary>
public enum GitResetMode
{
    Soft,
    Mixed,
    Hard
}

/// <summary>
/// Git tag command.
/// </summary>
public class TagCommand : GitCommand
{
    public string? Name { get; init; }
    public string? Message { get; init; }
    public bool Delete { get; init; }
    public bool List { get; init; }
    public string? Target { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "tag" };
        if (Delete)
        {
            args.Add("-d");
            if (Name != null) args.Add(Name);
            return args;
        }
        if (List)
        {
            args.Add("-l");
            return args;
        }
        if (Message != null)
        {
            args.Add("-a");
            if (Name != null) args.Add(Name);
            args.Add("-m");
            args.Add(Message);
        }
        else if (Name != null)
        {
            args.Add(Name);
        }
        if (Target != null) args.Add(Target);
        return args;
    }
}

/// <summary>
/// Git status command.
/// </summary>
public class StatusCommand : GitCommand
{
    public bool Porcelain { get; init; } = true;
    public bool Short { get; init; }
    public bool Branch { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "status" };
        if (Porcelain) args.Add("--porcelain");
        if (Short) args.Add("--short");
        if (Branch) args.Add("--branch");
        return args;
    }
}

/// <summary>
/// Git log command.
/// </summary>
public class LogCommand : GitCommand
{
    public int? MaxCount { get; init; }
    public string? Format { get; init; }
    public bool Oneline { get; init; }
    public string? Branch { get; init; }
    public IReadOnlyList<string>? Paths { get; init; }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "log" };
        if (Oneline) args.Add("--oneline");
        if (MaxCount.HasValue)
        {
            args.Add("-n");
            args.Add(MaxCount.Value.ToString());
        }
        if (Format != null)
        {
            args.Add($"--format={Format}");
        }
        if (Branch != null) args.Add(Branch);
        if (Paths != null && Paths.Count > 0)
        {
            args.Add("--");
            args.AddRange(Paths);
        }
        return args;
    }
}

/// <summary>
/// Git add command.
/// </summary>
public class AddCommand : GitCommand
{
    public IReadOnlyList<string> Paths { get; }
    public bool All { get; init; }
    public bool Update { get; init; }

    public AddCommand(params string[] paths)
    {
        Paths = paths;
    }

    public AddCommand(IReadOnlyList<string> paths)
    {
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "add" };
        if (All)
        {
            args.Add("--all");
            return args;
        }
        if (Update)
        {
            args.Add("--update");
            return args;
        }
        args.AddRange(Paths);
        return args;
    }
}

/// <summary>
/// Git commit command.
/// </summary>
public class CommitCommand : GitCommand
{
    public string Message { get; }
    public bool Amend { get; init; }
    public bool AllowEmpty { get; init; }

    public CommitCommand(string message)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "commit" };
        if (Amend) args.Add("--amend");
        if (AllowEmpty) args.Add("--allow-empty");
        args.Add("-m");
        args.Add(Message);
        return args;
    }
}

/// <summary>
/// Git revert command.
/// </summary>
public class RevertCommand : GitCommand
{
    public string Commit { get; }
    public bool NoCommit { get; init; }
    public int? MainlineParent { get; init; }
    public bool Abort { get; init; }
    public bool Continue { get; init; }

    public RevertCommand(string commit)
    {
        Commit = commit ?? throw new ArgumentNullException(nameof(commit));
    }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "revert" };
        if (Abort)
        {
            args.Add("--abort");
            return args;
        }
        if (Continue)
        {
            args.Add("--continue");
            return args;
        }
        if (NoCommit) args.Add("--no-commit");
        if (MainlineParent.HasValue)
        {
            args.Add("-m");
            args.Add(MainlineParent.Value.ToString());
        }
        args.Add(Commit);
        return args;
    }
}

/// <summary>
/// Git cherry-pick command.
/// </summary>
public class CherryPickCommand : GitCommand
{
    public string Commit { get; }
    public bool NoCommit { get; init; }
    public bool Abort { get; init; }
    public bool Continue { get; init; }

    public CherryPickCommand(string commit)
    {
        Commit = commit ?? throw new ArgumentNullException(nameof(commit));
    }

    public override IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "cherry-pick" };
        if (Abort)
        {
            args.Add("--abort");
            return args;
        }
        if (Continue)
        {
            args.Add("--continue");
            return args;
        }
        if (NoCommit) args.Add("--no-commit");
        args.Add(Commit);
        return args;
    }
}

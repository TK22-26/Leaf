#!/usr/bin/env bash
# create-merge-test-repos.sh — Deterministic test repo fixtures for Leaf merge/conflict testing
# Creates 35 repos at $BASE_DIR/test-merge-NN-description/
# Requires: Git Bash on Windows (Git for Windows)
set -euo pipefail

BASE_DIR="/c/Users/Tim/Documents/Repos/LeafTestRepos"

# Deterministic identity and timestamps
export GIT_AUTHOR_NAME="Test User"
export GIT_AUTHOR_EMAIL="test@leaf.dev"
export GIT_COMMITTER_NAME="Test User"
export GIT_COMMITTER_EMAIL="test@leaf.dev"

HOUR=0
next_date() {
    HOUR=$((HOUR + 1))
    local h=$(printf "%02d" $HOUR)
    export GIT_AUTHOR_DATE="2025-01-15T${h}:00:00"
    export GIT_COMMITTER_DATE="2025-01-15T${h}:00:00"
}

# --- Validation ---
validate_repo() {
    local name="$1" expected_branch="$2" sentinel="${3:-}" conflict_count="${4:-0}"
    cd "$BASE_DIR/$name"

    local actual_branch
    actual_branch=$(git branch --show-current 2>/dev/null || echo "")
    # Empty string from --show-current means detached HEAD (rebase, etc.)
    if [[ -z "$actual_branch" ]]; then
        actual_branch="DETACHED"
    fi
    if [[ "$actual_branch" != "$expected_branch" ]]; then
        echo "FAIL $name: branch=$actual_branch expected=$expected_branch"; exit 1
    fi

    if [[ -n "$sentinel" ]]; then
        if [[ ! -e ".git/$sentinel" ]]; then
            echo "FAIL $name: missing .git/$sentinel"; exit 1
        fi
    fi

    if [[ "$conflict_count" -gt 0 ]]; then
        local actual
        actual=$(git diff --name-only --diff-filter=U 2>/dev/null | wc -l | tr -d ' ')
        if [[ "$actual" -lt "$conflict_count" ]]; then
            echo "FAIL $name: conflicts=$actual expected>=$conflict_count"; exit 1
        fi
    fi

    echo "OK $name"
}

init_repo() {
    local name="$1"
    rm -rf "$BASE_DIR/$name"
    mkdir -p "$BASE_DIR/$name"
    cd "$BASE_DIR/$name"
    git init -b main
    git config core.autocrlf false
    git config user.name "Test User"
    git config user.email "test@leaf.dev"
}

# ===========================================================================
# A. Merge Types — Clean state, ready to merge
# ===========================================================================

create_01_normal_merge_clean() {
    init_repo "test-merge-01-normal-merge-clean"

    next_date
    echo "class Calculator { public int Add(int a, int b) => a + b; }" > Calculator.cs
    git add Calculator.cs && git commit -m "Initial calculator"

    next_date
    git checkout -b feature/validation
    echo "class Validator { public bool IsPositive(int n) => n > 0; }" > Validator.cs
    git add Validator.cs && git commit -m "Add validator"

    next_date
    echo "class Validator { public bool IsPositive(int n) => n > 0; public bool IsNonNeg(int n) => n >= 0; }" > Validator.cs
    git add Validator.cs && git commit -m "Add IsNonNeg"

    git checkout main

    validate_repo "test-merge-01-normal-merge-clean" "main"
}

create_02_fast_forward() {
    init_repo "test-merge-02-fast-forward"

    next_date
    echo "line 1" > file.txt
    git add file.txt && git commit -m "Initial"

    next_date
    git checkout -b feature/ahead
    echo "line 2" >> file.txt
    git add file.txt && git commit -m "Add line 2"

    next_date
    echo "line 3" >> file.txt
    git add file.txt && git commit -m "Add line 3"

    git checkout main

    validate_repo "test-merge-02-fast-forward" "main"
}

create_03_ff_not_possible() {
    init_repo "test-merge-03-ff-not-possible"

    next_date
    echo "base" > shared.txt
    git add shared.txt && git commit -m "Initial"

    next_date
    git checkout -b feature/diverged
    echo "feature work" > feature.txt
    git add feature.txt && git commit -m "Feature work"

    next_date
    git checkout main
    echo "main work" > main-only.txt
    git add main-only.txt && git commit -m "Main work"

    validate_repo "test-merge-03-ff-not-possible" "main"
}

create_04_squash_merge() {
    init_repo "test-merge-04-squash-merge"

    next_date
    echo "base" > file.txt
    git add file.txt && git commit -m "Initial"

    next_date
    git checkout -b feature/multi-commit
    echo "change 1" > a.txt
    git add a.txt && git commit -m "Commit 1"

    next_date
    echo "change 2" > b.txt
    git add b.txt && git commit -m "Commit 2"

    next_date
    echo "change 3" > c.txt
    git add c.txt && git commit -m "Commit 3"

    git checkout main

    validate_repo "test-merge-04-squash-merge" "main"
}

create_05_already_up_to_date() {
    init_repo "test-merge-05-already-up-to-date"

    next_date
    echo "base" > file.txt
    git add file.txt && git commit -m "Initial"

    next_date
    git checkout -b feature/done
    echo "done" > done.txt
    git add done.txt && git commit -m "Feature done"

    next_date
    git checkout main
    git merge --no-ff feature/done -m "Merge feature/done"

    validate_repo "test-merge-05-already-up-to-date" "main"
}

# ===========================================================================
# B. Conflict Varieties — Left in MERGE conflict state
# ===========================================================================

create_06_single_file_conflict() {
    init_repo "test-merge-06-single-file-conflict"

    next_date
    cat > Calculator.cs << 'CSEOF'
public class Calculator
{
    public int Add(int a, int b)
    {
        return a + b;
    }
}
CSEOF
    git add Calculator.cs && git commit -m "Initial calculator"

    next_date
    git checkout -b feature/validation
    cat > Calculator.cs << 'CSEOF'
public class Calculator
{
    public int Add(int a, int b)
    {
        if (a < 0 || b < 0) throw new ArgumentException("Negative");
        return a + b;
    }
}
CSEOF
    git add Calculator.cs && git commit -m "Add validation"

    next_date
    git checkout main
    cat > Calculator.cs << 'CSEOF'
public class Calculator
{
    public int Add(int a, int b)
    {
        // Optimized addition
        return checked(a + b);
    }
}
CSEOF
    git add Calculator.cs && git commit -m "Optimize addition"

    # Trigger merge conflict
    git merge feature/validation --no-ff -m "Merge feature/validation" || true

    validate_repo "test-merge-06-single-file-conflict" "main" "MERGE_HEAD" 1
}

create_07_multi_file_conflict() {
    init_repo "test-merge-07-multi-file-conflict"

    next_date
    echo "module A v1" > moduleA.cs
    echo "module B v1" > moduleB.cs
    echo "module C v1" > moduleC.cs
    echo "shared" > shared.txt
    git add -A && git commit -m "Initial modules"

    next_date
    git checkout -b feature/refactor
    echo "module A v2-feature" > moduleA.cs
    echo "module B v2-feature" > moduleB.cs
    echo "module C v2-feature" > moduleC.cs
    git add -A && git commit -m "Refactor all modules"

    next_date
    git checkout main
    echo "module A v2-main" > moduleA.cs
    echo "module B v2-main" > moduleB.cs
    echo "module C v2-main" > moduleC.cs
    git add -A && git commit -m "Update all modules on main"

    git merge feature/refactor --no-ff -m "Merge feature/refactor" || true

    validate_repo "test-merge-07-multi-file-conflict" "main" "MERGE_HEAD" 3
}

create_08_multi_region_conflict() {
    init_repo "test-merge-08-multi-region-conflict"

    next_date
    cat > service.cs << 'CSEOF'
public class Service
{
    public void MethodA()
    {
        Console.WriteLine("A original");
    }

    public void MethodB()
    {
        Console.WriteLine("B original");
    }

    public void MethodC()
    {
        Console.WriteLine("C shared - no conflict");
    }

    public void MethodD()
    {
        Console.WriteLine("D original");
    }
}
CSEOF
    git add service.cs && git commit -m "Initial service"

    next_date
    git checkout -b feature/impl
    cat > service.cs << 'CSEOF'
public class Service
{
    public void MethodA()
    {
        Console.WriteLine("A feature");
    }

    public void MethodB()
    {
        Console.WriteLine("B feature");
    }

    public void MethodC()
    {
        Console.WriteLine("C shared - no conflict");
    }

    public void MethodD()
    {
        Console.WriteLine("D feature");
    }
}
CSEOF
    git add service.cs && git commit -m "Feature implementation"

    next_date
    git checkout main
    cat > service.cs << 'CSEOF'
public class Service
{
    public void MethodA()
    {
        Console.WriteLine("A main");
    }

    public void MethodB()
    {
        Console.WriteLine("B main");
    }

    public void MethodC()
    {
        Console.WriteLine("C shared - no conflict");
    }

    public void MethodD()
    {
        Console.WriteLine("D main");
    }
}
CSEOF
    git add service.cs && git commit -m "Main changes"

    git merge feature/impl --no-ff -m "Merge feature/impl" || true

    validate_repo "test-merge-08-multi-region-conflict" "main" "MERGE_HEAD" 1
}

create_09_modify_delete_conflict() {
    init_repo "test-merge-09-modify-delete-conflict"

    next_date
    echo "original content" > target.cs
    echo "other file" > other.txt
    git add -A && git commit -m "Initial"

    next_date
    git checkout -b feature/delete
    git rm target.cs
    git commit -m "Delete target.cs"

    next_date
    git checkout main
    echo "modified content on main" > target.cs
    git add target.cs && git commit -m "Modify target.cs"

    git merge feature/delete --no-ff -m "Merge feature/delete" || true

    validate_repo "test-merge-09-modify-delete-conflict" "main" "MERGE_HEAD" 1
}

create_10_both_added_conflict() {
    init_repo "test-merge-10-both-added-conflict"

    next_date
    echo "base" > base.txt
    git add base.txt && git commit -m "Initial"

    next_date
    git checkout -b feature/add
    echo "feature version of newfile" > newfile.cs
    git add newfile.cs && git commit -m "Add newfile (feature)"

    next_date
    git checkout main
    echo "main version of newfile" > newfile.cs
    git add newfile.cs && git commit -m "Add newfile (main)"

    git merge feature/add --no-ff -m "Merge feature/add" || true

    validate_repo "test-merge-10-both-added-conflict" "main" "MERGE_HEAD" 1
}

create_11_rename_content_conflict() {
    init_repo "test-merge-11-rename-content-conflict"

    next_date
    echo "original content line 1" > original.cs
    git add original.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/rename
    git mv original.cs renamed.cs
    echo "feature content line 1" > renamed.cs
    git add renamed.cs && git commit -m "Rename + edit (feature)"

    next_date
    git checkout main
    git mv original.cs renamed.cs
    echo "main content line 1" > renamed.cs
    git add renamed.cs && git commit -m "Rename + edit (main)"

    git merge feature/rename --no-ff -m "Merge feature/rename" || true

    validate_repo "test-merge-11-rename-content-conflict" "main" "MERGE_HEAD" 1
}

create_12_binary_file_conflict() {
    init_repo "test-merge-12-binary-file-conflict"

    next_date
    # Create a small PNG-like binary blob
    printf '\x89PNG\r\n\x1a\nBASE_IMAGE_DATA_V1' > image.png
    echo "readme v1" > readme.txt
    git add -A && git commit -m "Initial with binary"

    next_date
    git checkout -b feature/new-image
    printf '\x89PNG\r\n\x1a\nFEATURE_IMAGE_DATA_V2' > image.png
    echo "readme v2 feature" > readme.txt
    git add -A && git commit -m "Update image (feature)"

    next_date
    git checkout main
    printf '\x89PNG\r\n\x1a\nMAIN_IMAGE_DATA_V2' > image.png
    echo "readme v2 main" > readme.txt
    git add -A && git commit -m "Update image (main)"

    git merge feature/new-image --no-ff -m "Merge feature/new-image" || true

    validate_repo "test-merge-12-binary-file-conflict" "main" "MERGE_HEAD"
}

create_13_large_conflict() {
    init_repo "test-merge-13-large-conflict"

    next_date
    # Generate a file with many sections that will each conflict
    {
        for i in $(seq 1 10); do
            echo "// Section $i"
            echo "public void Method$i()"
            echo "{"
            echo "    // Original implementation $i"
            echo "    Console.WriteLine(\"section $i original\");"
            echo "}"
            echo ""
        done
    } > bigfile.cs
    git add bigfile.cs && git commit -m "Initial big file"

    next_date
    git checkout -b feature/rewrite
    {
        for i in $(seq 1 10); do
            echo "// Section $i"
            echo "public void Method$i()"
            echo "{"
            echo "    // Feature implementation $i"
            echo "    Console.WriteLine(\"section $i feature\");"
            echo "}"
            echo ""
        done
    } > bigfile.cs
    git add bigfile.cs && git commit -m "Feature rewrite"

    next_date
    git checkout main
    {
        for i in $(seq 1 10); do
            echo "// Section $i"
            echo "public void Method$i()"
            echo "{"
            echo "    // Main implementation $i"
            echo "    Console.WriteLine(\"section $i main\");"
            echo "}"
            echo ""
        done
    } > bigfile.cs
    git add bigfile.cs && git commit -m "Main rewrite"

    git merge feature/rewrite --no-ff -m "Merge feature/rewrite" || true

    validate_repo "test-merge-13-large-conflict" "main" "MERGE_HEAD" 1
}

# ===========================================================================
# C. Abort/Cancel Flows — Left in conflict state
# ===========================================================================

create_14_abort_merge() {
    init_repo "test-merge-14-abort-merge"

    next_date
    echo "original" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/change
    echo "feature version" > file.cs
    git add file.cs && git commit -m "Feature change"

    next_date
    git checkout main
    echo "main version" > file.cs
    git add file.cs && git commit -m "Main change"

    git merge feature/change --no-ff -m "Merge" || true

    validate_repo "test-merge-14-abort-merge" "main" "MERGE_HEAD" 1
}

create_15_abort_after_partial_resolve() {
    init_repo "test-merge-15-abort-after-partial-resolve"

    next_date
    echo "a v1" > a.cs
    echo "b v1" > b.cs
    echo "c v1" > c.cs
    git add -A && git commit -m "Initial"

    next_date
    git checkout -b feature/all
    echo "a v2-feature" > a.cs
    echo "b v2-feature" > b.cs
    echo "c v2-feature" > c.cs
    git add -A && git commit -m "Feature changes"

    next_date
    git checkout main
    echo "a v2-main" > a.cs
    echo "b v2-main" > b.cs
    echo "c v2-main" > c.cs
    git add -A && git commit -m "Main changes"

    git merge feature/all --no-ff -m "Merge" || true

    # Resolve one file to simulate partial resolution
    echo "a resolved" > a.cs
    git add a.cs

    validate_repo "test-merge-15-abort-after-partial-resolve" "main" "MERGE_HEAD" 2
}

create_16_abort_cherry_pick() {
    init_repo "test-merge-16-abort-cherry-pick"

    next_date
    echo "original" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/cp
    echo "cherry-pick version" > file.cs
    git add file.cs && git commit -m "Cherry-pick source"
    local cp_sha
    cp_sha=$(git rev-parse HEAD)

    next_date
    git checkout main
    echo "main version" > file.cs
    git add file.cs && git commit -m "Main diverged"

    # Cherry-pick that will conflict
    git cherry-pick "$cp_sha" || true

    validate_repo "test-merge-16-abort-cherry-pick" "main" "CHERRY_PICK_HEAD" 1
}

create_17_squash_merge_conflict() {
    init_repo "test-merge-17-squash-merge-conflict"

    next_date
    echo "original" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/squash
    echo "squash version" > file.cs
    git add file.cs && git commit -m "Squash source"

    next_date
    git checkout main
    echo "main version" > file.cs
    git add file.cs && git commit -m "Main diverged"

    # Squash merge that conflicts — note: no MERGE_HEAD for squash
    git merge --squash feature/squash || true

    validate_repo "test-merge-17-squash-merge-conflict" "main" "" 1
}

create_18_abort_revert() {
    init_repo "test-merge-18-abort-revert"

    next_date
    echo "original content" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    echo "changed content" > file.cs
    git add file.cs && git commit -m "Change to revert"
    local revert_sha
    revert_sha=$(git rev-parse HEAD)

    next_date
    echo "further changes building on previous" > file.cs
    git add file.cs && git commit -m "Further changes"

    # Revert the middle commit — will conflict with "further changes"
    git revert --no-edit "$revert_sha" || true

    validate_repo "test-merge-18-abort-revert" "main" "REVERT_HEAD" 1
}

# ===========================================================================
# D. Rebase Scenarios — Left in rebase state
# ===========================================================================

create_19_rebase_single_conflict() {
    init_repo "test-merge-19-rebase-single-conflict"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/rebase
    echo "feature change" > file.cs
    git add file.cs && git commit -m "Feature on rebase branch"

    next_date
    git checkout main
    echo "main change" > file.cs
    git add file.cs && git commit -m "Main change"

    # Start rebase that will conflict
    git checkout feature/rebase
    git rebase main || true

    validate_repo "test-merge-19-rebase-single-conflict" "DETACHED" "rebase-merge" 1
}

create_20_rebase_sequential_conflicts() {
    init_repo "test-merge-20-rebase-sequential-conflicts"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/multi-rebase
    echo "feature commit 1" > file.cs
    git add file.cs && git commit -m "Feature commit 1"

    next_date
    echo "feature commit 2" > file.cs
    git add file.cs && git commit -m "Feature commit 2"

    next_date
    git checkout main
    echo "main change" > file.cs
    git add file.cs && git commit -m "Main change"

    # Start rebase — first commit will conflict
    git checkout feature/multi-rebase
    git rebase main || true

    validate_repo "test-merge-20-rebase-sequential-conflicts" "DETACHED" "rebase-merge" 1
}

create_21_rebase_abort() {
    init_repo "test-merge-21-rebase-abort"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/abort-rebase
    echo "feature" > file.cs
    git add file.cs && git commit -m "Feature change"

    next_date
    git checkout main
    echo "main" > file.cs
    git add file.cs && git commit -m "Main change"

    git checkout feature/abort-rebase
    git rebase main || true

    validate_repo "test-merge-21-rebase-abort" "DETACHED" "rebase-merge" 1
}

create_22_rebase_skip() {
    init_repo "test-merge-22-rebase-skip"

    next_date
    echo "base" > file.cs
    echo "other" > other.txt
    git add -A && git commit -m "Initial"

    next_date
    git checkout -b feature/skip
    echo "conflict commit" > file.cs
    git add file.cs && git commit -m "Conflicting commit (to skip)"

    next_date
    echo "good commit" > other.txt
    git add other.txt && git commit -m "Good commit (to keep)"

    next_date
    git checkout main
    echo "main" > file.cs
    git add file.cs && git commit -m "Main change"

    git checkout feature/skip
    git rebase main || true

    validate_repo "test-merge-22-rebase-skip" "DETACHED" "rebase-merge" 1
}

create_23_rebase_abort_after_continue() {
    init_repo "test-merge-23-rebase-abort-after-continue"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/multi-rebase-abort
    echo "feature v1" > file.cs
    git add file.cs && git commit -m "Feature commit 1"

    next_date
    echo "feature v2" > file.cs
    git add file.cs && git commit -m "Feature commit 2"

    next_date
    echo "feature v3" > file.cs
    git add file.cs && git commit -m "Feature commit 3"

    next_date
    git checkout main
    echo "main" > file.cs
    git add file.cs && git commit -m "Main change"

    # Start rebase — first commit will conflict, resolve + continue, second will also conflict
    git checkout feature/multi-rebase-abort
    git rebase main || true

    # Resolve first conflict and continue to get second conflict
    echo "resolved v1" > file.cs
    git add file.cs
    git rebase --continue || true

    validate_repo "test-merge-23-rebase-abort-after-continue" "DETACHED" "rebase-merge" 1
}

# ===========================================================================
# E. Cherry-Pick & Revert
# ===========================================================================

create_24_cherry_pick_conflict() {
    init_repo "test-merge-24-cherry-pick-conflict"

    next_date
    echo "base content" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/cp-source
    echo "cherry-pick content" > file.cs
    git add file.cs && git commit -m "Cherry-pick source commit"
    local cp_sha
    cp_sha=$(git rev-parse HEAD)

    next_date
    git checkout main
    echo "main content diverged" > file.cs
    git add file.cs && git commit -m "Main diverged"

    git cherry-pick "$cp_sha" || true

    validate_repo "test-merge-24-cherry-pick-conflict" "main" "CHERRY_PICK_HEAD" 1
}

create_25_cherry_pick_clean() {
    init_repo "test-merge-25-cherry-pick-clean"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/cp-clean
    echo "new feature file" > feature.cs
    git add feature.cs && git commit -m "Add feature file"

    git checkout main
    # cp_sha will be cherry-picked in the test — leave ready state

    validate_repo "test-merge-25-cherry-pick-clean" "main"
}

create_26_revert_conflict() {
    init_repo "test-merge-26-revert-conflict"

    next_date
    echo "line 1" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    echo "line 1 modified" > file.cs
    git add file.cs && git commit -m "Modify line 1"
    local revert_sha
    revert_sha=$(git rev-parse HEAD)

    next_date
    echo "line 1 modified further" > file.cs
    git add file.cs && git commit -m "Modify further"

    # Revert middle commit — will conflict
    git revert --no-edit "$revert_sha" || true

    validate_repo "test-merge-26-revert-conflict" "main" "REVERT_HEAD" 1
}

create_27_revert_clean() {
    init_repo "test-merge-27-revert-clean"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    echo "added line" >> file.cs
    git add file.cs && git commit -m "Add line (to revert)"

    # Leave ready — the last commit can be cleanly reverted

    validate_repo "test-merge-27-revert-clean" "main"
}

# ===========================================================================
# F. Edge Cases
# ===========================================================================

create_28_orphaned_conflicts() {
    init_repo "test-merge-28-orphaned-conflicts"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/orphan
    echo "feature" > file.cs
    git add file.cs && git commit -m "Feature"

    next_date
    git checkout main
    echo "main" > file.cs
    git add file.cs && git commit -m "Main"

    # Create merge conflict, then manually remove MERGE_HEAD to simulate orphaned state
    git merge feature/orphan --no-ff -m "Merge" || true
    rm -f .git/MERGE_HEAD

    validate_repo "test-merge-28-orphaned-conflicts" "main" "" 1
}

create_29_unrelated_histories() {
    init_repo "test-merge-29-unrelated-histories"

    next_date
    echo "main content" > main.txt
    git add main.txt && git commit -m "Main initial"

    # Create orphan branch (no common ancestor)
    next_date
    git checkout --orphan unrelated
    git rm -rf .
    echo "unrelated content" > unrelated.txt
    git add unrelated.txt && git commit -m "Unrelated initial"

    git checkout main

    validate_repo "test-merge-29-unrelated-histories" "main"
}

create_30_conflict_markers_in_content() {
    init_repo "test-merge-30-conflict-markers-in-content"

    next_date
    cat > parser.cs << 'CSEOF'
public class Parser
{
    // This code legitimately contains merge marker-like strings
    private static readonly string[] ConflictMarkers = new[]
    {
        "<<<<<<<",
        "=======",
        ">>>>>>>"
    };

    public bool HasConflicts(string text)
    {
        return ConflictMarkers.Any(m => text.Contains(m));
    }
}
CSEOF
    git add parser.cs && git commit -m "Initial parser with marker strings"

    next_date
    git checkout -b feature/extend-parser
    cat > parser.cs << 'CSEOF'
public class Parser
{
    // This code legitimately contains merge marker-like strings
    private static readonly string[] ConflictMarkers = new[]
    {
        "<<<<<<<",
        "=======",
        ">>>>>>>"
    };

    public bool HasConflicts(string text)
    {
        return ConflictMarkers.Any(m => text.Contains(m));
    }

    public int CountConflicts(string text)
    {
        // Feature version
        return text.Split("<<<<<<<").Length - 1;
    }
}
CSEOF
    git add parser.cs && git commit -m "Add CountConflicts (feature)"

    next_date
    git checkout main
    cat > parser.cs << 'CSEOF'
public class Parser
{
    // This code legitimately contains merge marker-like strings
    private static readonly string[] ConflictMarkers = new[]
    {
        "<<<<<<<",
        "=======",
        ">>>>>>>"
    };

    public bool HasConflicts(string text)
    {
        return ConflictMarkers.Any(m => text.Contains(m));
    }

    public int CountConflicts(string text)
    {
        // Main version
        return text.Split(">>>>>>>").Length - 1;
    }
}
CSEOF
    git add parser.cs && git commit -m "Add CountConflicts (main)"

    git merge feature/extend-parser --no-ff -m "Merge" || true

    validate_repo "test-merge-30-conflict-markers-in-content" "main" "MERGE_HEAD" 1
}

create_31_crlf_lf_conflict() {
    init_repo "test-merge-31-crlf-lf-conflict"

    next_date
    printf "line1\nline2\nline3\n" > file.cs
    git add file.cs && git commit -m "Initial LF"

    next_date
    git checkout -b feature/crlf
    printf "line1 feature\r\nline2\r\nline3\r\n" > file.cs
    git add file.cs && git commit -m "Feature with CRLF"

    next_date
    git checkout main
    printf "line1 main\nline2\nline3\n" > file.cs
    git add file.cs && git commit -m "Main with LF"

    git merge feature/crlf --no-ff -m "Merge" || true

    validate_repo "test-merge-31-crlf-lf-conflict" "main" "MERGE_HEAD" 1
}

# ===========================================================================
# G. State & Persistence
# ===========================================================================

create_32_stale_leaf_merge_file() {
    init_repo "test-merge-32-stale-leaf-merge-file"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/stale
    echo "feature" > file.cs
    git add file.cs && git commit -m "Feature"

    next_date
    git checkout main
    echo "main" > file.cs
    git add file.cs && git commit -m "Main"

    # Plant a stale leaf-merge-conflicts.txt before merging
    echo "old-file-from-previous-merge.cs" > .git/leaf-merge-conflicts.txt

    git merge feature/stale --no-ff -m "Merge" || true

    validate_repo "test-merge-32-stale-leaf-merge-file" "main" "MERGE_HEAD" 1
}

create_33_reopen_during_merge() {
    init_repo "test-merge-33-reopen-during-merge"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/reopen
    echo "feature" > file.cs
    git add file.cs && git commit -m "Feature"

    next_date
    git checkout main
    echo "main" > file.cs
    git add file.cs && git commit -m "Main"

    git merge feature/reopen --no-ff -m "Merge" || true

    validate_repo "test-merge-33-reopen-during-merge" "main" "MERGE_HEAD" 1
}

create_34_repo_switch_during_conflict() {
    # Repo A: in conflict
    init_repo "test-merge-34-repo-switch-during-conflict-A"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/conflict
    echo "feature" > file.cs
    git add file.cs && git commit -m "Feature"

    next_date
    git checkout main
    echo "main" > file.cs
    git add file.cs && git commit -m "Main"

    git merge feature/conflict --no-ff -m "Merge" || true

    validate_repo "test-merge-34-repo-switch-during-conflict-A" "main" "MERGE_HEAD" 1

    # Repo B: clean
    init_repo "test-merge-34-repo-switch-during-conflict-B"

    next_date
    echo "clean repo" > file.txt
    git add file.txt && git commit -m "Clean repo"

    validate_repo "test-merge-34-repo-switch-during-conflict-B" "main"
}

# ===========================================================================
# Additional: Interactive Rebase External
# ===========================================================================

create_35_interactive_rebase_external() {
    init_repo "test-merge-35-interactive-rebase-external"

    next_date
    echo "base" > file.cs
    git add file.cs && git commit -m "Initial"

    next_date
    git checkout -b feature/interactive
    echo "commit 1" > file.cs
    git add file.cs && git commit -m "Commit 1"

    next_date
    echo "commit 2" > file.cs
    git add file.cs && git commit -m "Commit 2"

    next_date
    git checkout main
    echo "main" > file.cs
    git add file.cs && git commit -m "Main change"

    # Simulate interactive rebase state by starting a non-interactive rebase
    # that conflicts (creates rebase-merge directory, same as interactive)
    git checkout feature/interactive
    git rebase main || true

    validate_repo "test-merge-35-interactive-rebase-external" "DETACHED" "rebase-merge" 1
}

# ===========================================================================
# Generate manifest.json
# ===========================================================================

generate_manifest() {
    cat > "$BASE_DIR/manifest.json" << 'MANIFESTEOF'
[
  {
    "name": "test-merge-01-normal-merge-clean",
    "category": "Resolution flow",
    "state": "ready-to-merge",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": true
  },
  {
    "name": "test-merge-02-fast-forward",
    "category": "Resolution flow",
    "state": "ready-to-merge",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": "fast-forward",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-03-ff-not-possible",
    "category": "UI detection",
    "state": "ready-to-merge",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": "fast-forward-fail",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-04-squash-merge",
    "category": "Resolution flow",
    "state": "ready-to-merge",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": "squash",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-05-already-up-to-date",
    "category": "UI detection",
    "state": "already-merged",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-06-single-file-conflict",
    "category": "UI detection + Resolution",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": true
  },
  {
    "name": "test-merge-07-multi-file-conflict",
    "category": "Resolution flow",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 3,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-08-multi-region-conflict",
    "category": "Resolution flow",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": true
  },
  {
    "name": "test-merge-09-modify-delete-conflict",
    "category": "UI detection",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-10-both-added-conflict",
    "category": "UI detection",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-11-rename-content-conflict",
    "category": "UI detection",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 0,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-12-binary-file-conflict",
    "category": "UI detection",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 0,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-13-large-conflict",
    "category": "Resolution flow",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-14-abort-merge",
    "category": "Abort/cleanup",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": true
  },
  {
    "name": "test-merge-15-abort-after-partial-resolve",
    "category": "Abort/cleanup",
    "state": "merge-conflict-partial",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 2,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-16-abort-cherry-pick",
    "category": "Abort/cleanup",
    "state": "cherry-pick-conflict",
    "sentinel": "CHERRY_PICK_HEAD",
    "conflictCount": 1,
    "operationType": "cherry-pick",
    "knownFails": ["B1", "B2"],
    "smokeTest": false
  },
  {
    "name": "test-merge-17-squash-merge-conflict",
    "category": "Abort/cleanup",
    "state": "squash-conflict",
    "sentinel": null,
    "conflictCount": 1,
    "operationType": "squash",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-18-abort-revert",
    "category": "Abort/cleanup",
    "state": "revert-conflict",
    "sentinel": "REVERT_HEAD",
    "conflictCount": 1,
    "operationType": "revert",
    "knownFails": ["B1", "B3", "B4"],
    "smokeTest": false
  },
  {
    "name": "test-merge-19-rebase-single-conflict",
    "category": "Resolution flow",
    "state": "rebase-conflict",
    "sentinel": "rebase-merge",
    "conflictCount": 1,
    "operationType": "rebase",
    "knownFails": [],
    "smokeTest": true
  },
  {
    "name": "test-merge-20-rebase-sequential-conflicts",
    "category": "Resolution flow",
    "state": "rebase-conflict",
    "sentinel": "rebase-merge",
    "conflictCount": 1,
    "operationType": "rebase",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-21-rebase-abort",
    "category": "Abort/cleanup",
    "state": "rebase-conflict",
    "sentinel": "rebase-merge",
    "conflictCount": 1,
    "operationType": "rebase",
    "knownFails": ["B1", "B5"],
    "smokeTest": false
  },
  {
    "name": "test-merge-22-rebase-skip",
    "category": "Abort/cleanup",
    "state": "rebase-conflict",
    "sentinel": "rebase-merge",
    "conflictCount": 1,
    "operationType": "rebase",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-23-rebase-abort-after-continue",
    "category": "Abort/cleanup",
    "state": "rebase-conflict",
    "sentinel": "rebase-merge",
    "conflictCount": 1,
    "operationType": "rebase",
    "knownFails": ["B1", "B5"],
    "smokeTest": false
  },
  {
    "name": "test-merge-24-cherry-pick-conflict",
    "category": "UI detection",
    "state": "cherry-pick-conflict",
    "sentinel": "CHERRY_PICK_HEAD",
    "conflictCount": 1,
    "operationType": "cherry-pick",
    "knownFails": ["B6"],
    "smokeTest": true
  },
  {
    "name": "test-merge-25-cherry-pick-clean",
    "category": "Resolution flow",
    "state": "ready-to-cherry-pick",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": "cherry-pick",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-26-revert-conflict",
    "category": "UI detection",
    "state": "revert-conflict",
    "sentinel": "REVERT_HEAD",
    "conflictCount": 1,
    "operationType": "revert",
    "knownFails": ["B3", "B4"],
    "smokeTest": false
  },
  {
    "name": "test-merge-27-revert-clean",
    "category": "Resolution flow",
    "state": "ready-to-revert",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": "revert",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-28-orphaned-conflicts",
    "category": "Persistence/state",
    "state": "orphaned-conflict",
    "sentinel": null,
    "conflictCount": 1,
    "operationType": "recovery",
    "knownFails": [],
    "smokeTest": true
  },
  {
    "name": "test-merge-29-unrelated-histories",
    "category": "UI detection",
    "state": "ready-to-merge",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-30-conflict-markers-in-content",
    "category": "UI detection",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": ["B10"],
    "smokeTest": false
  },
  {
    "name": "test-merge-31-crlf-lf-conflict",
    "category": "UI detection",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": [],
    "smokeTest": false
  },
  {
    "name": "test-merge-32-stale-leaf-merge-file",
    "category": "Persistence/state",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": ["B7"],
    "smokeTest": false
  },
  {
    "name": "test-merge-33-reopen-during-merge",
    "category": "Persistence/state",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": ["B7"],
    "smokeTest": false
  },
  {
    "name": "test-merge-34-repo-switch-during-conflict-A",
    "category": "Persistence/state",
    "state": "merge-conflict",
    "sentinel": "MERGE_HEAD",
    "conflictCount": 1,
    "operationType": "merge",
    "knownFails": ["B11"],
    "smokeTest": true
  },
  {
    "name": "test-merge-34-repo-switch-during-conflict-B",
    "category": "Persistence/state",
    "state": "clean",
    "sentinel": null,
    "conflictCount": 0,
    "operationType": null,
    "knownFails": [],
    "smokeTest": true
  },
  {
    "name": "test-merge-35-interactive-rebase-external",
    "category": "UI detection",
    "state": "rebase-conflict",
    "sentinel": "rebase-merge",
    "conflictCount": 1,
    "operationType": "rebase",
    "knownFails": ["B5"],
    "smokeTest": false
  }
]
MANIFESTEOF
    echo "OK manifest.json"
}

# ===========================================================================
# Main
# ===========================================================================

echo "Creating merge test repos in $BASE_DIR..."
echo "=========================================="

mkdir -p "$BASE_DIR"

create_01_normal_merge_clean
create_02_fast_forward
create_03_ff_not_possible
create_04_squash_merge
create_05_already_up_to_date
create_06_single_file_conflict
create_07_multi_file_conflict
create_08_multi_region_conflict
create_09_modify_delete_conflict
create_10_both_added_conflict
create_11_rename_content_conflict
create_12_binary_file_conflict
create_13_large_conflict
create_14_abort_merge
create_15_abort_after_partial_resolve
create_16_abort_cherry_pick
create_17_squash_merge_conflict
create_18_abort_revert
create_19_rebase_single_conflict
create_20_rebase_sequential_conflicts
create_21_rebase_abort
create_22_rebase_skip
create_23_rebase_abort_after_continue
create_24_cherry_pick_conflict
create_25_cherry_pick_clean
create_26_revert_conflict
create_27_revert_clean
create_28_orphaned_conflicts
create_29_unrelated_histories
create_30_conflict_markers_in_content
create_31_crlf_lf_conflict
create_32_stale_leaf_merge_file
create_33_reopen_during_merge
create_34_repo_switch_during_conflict
create_35_interactive_rebase_external

generate_manifest

echo ""
echo "=========================================="
echo "All repos created and validated successfully!"
echo "Manifest: $BASE_DIR/manifest.json"

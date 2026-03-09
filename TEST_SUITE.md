# Test Suite — March 8, 2026 Fixes

Manual verification checklist for all issues closed today. Test on a repo with multiple branches, stashes, and remote tracking.

---

## #22 — Creating new branches is broken
**Fix:** `bc4c5d3` Fire branch popup event when showing branch input

- [ ] Right-click a commit in the graph → "Create Branch Here" → branch name dialog appears
- [ ] Type a name and confirm → new branch is created at that commit
- [ ] Right-click a branch label → "New Branch from..." → dialog appears and works
- [ ] Cancel the dialog → no branch created, no error

---

## #23 — Changed Files count wrong for renamed/moved files
**Fix:** `26988dc` Show renamed files count in commit detail view

- [ ] Find a commit that renames or moves files (e.g. file restructuring)
- [ ] "Changed Files (N)" count matches the actual sum of modified + added + deleted + renamed
- [ ] Breakdown stats include a renamed count (arrow icon →) when renames exist
- [ ] Status icon in the file tree shows the correct icon for renamed files
- [ ] A commit with no renames shows the same breakdown as before (no "0 →" clutter)

---

## #21 — Moving to a remote branch needs to pull
**Fix:** `453621f` Pull remote branch when local behind on checkout

- [ ] Have a local branch that is behind its remote tracking branch
- [ ] Double-click the remote branch in the branch list to check it out
- [ ] Verify the local branch is pulled up to the remote commit (not left behind)
- [ ] Check `git log` — local branch tip matches the remote branch tip
- [ ] If already up-to-date, double-clicking remote branch just checks out (no unnecessary pull)

---

## #20 — Push errors should display somewhere
**Fix:** `1ebceb6` Show push failure details before refreshing

- [ ] Push to a branch with a branch policy (requires PR) — verify an error popup/notification appears with the actual error message
- [ ] Push to a branch where the remote rejects (e.g. force-push required) — verify the error is displayed
- [ ] Successful push — no error shown, normal refresh
- [ ] Error message is readable and includes the reason (e.g. "branch policy requiring pull requests")

---

## #19 — Deleting a viewed file
**Fix:** `9cef408` Handle diff viewer when files are deleted

- [ ] Open a commit, click a file to view its diff
- [ ] While viewing that file, delete it from disk (or stage a deletion)
- [ ] Verify Leaf closes the file view and returns to the commit graph (no crash, no blank pane)
- [ ] Delete a file you are NOT viewing — no navigation change, no crash

---

## #18 — TreeView bad paddings on commit file view
**Fix:** Included in the commit detail view updates

- [ ] Select a commit with files
- [ ] Expand the file tree — verify items are evenly spaced with consistent padding
- [ ] Compare padding between root items and nested items — should look uniform
- [ ] Resize the panel — padding should not break or clip text

---

## #17 — Ahead/behind arrows on branch list
**Fix:** `2ae014e` Show ahead/behind badges on branch list view

- [ ] Open a repo where local branches are ahead of and/or behind their remote
- [ ] Verify the branch list shows up/down arrow badges with counts (e.g. ↑2 ↓3)
- [ ] A branch that is in sync with remote shows no arrows (or 0/0 — verify it's clean)
- [ ] A branch with no remote tracking shows no arrows
- [ ] Push commits to make a branch ahead — verify the count updates after refresh
- [ ] Pull commits to make a branch behind — verify the count updates after refresh

---

## #16 — Stashes inline on branch graph
**Fix:** `d80006c` Treat stash rows as inline graph nodes

- [ ] Create a stash on the current branch
- [ ] Verify the stash appears as a special node in the graph, positioned at the top of the branch it was stashed on (not in a separate stash section)
- [ ] Stash node has a distinct visual (rounded rectangle with stash icon, not a regular commit circle)
- [ ] Stash connection line goes to the correct parent commit (the commit it was stashed on)
- [ ] Multiple stashes on the same branch stack correctly
- [ ] Stash on a different branch appears on that branch's lane
- [ ] Apply/drop a stash — graph updates, stash node removed

---

## #15 — Show all files toggle
**Fix:** `48f48ce` Add show-all-files toggle to commit detail view

- [ ] Select a commit in the graph
- [ ] In the file tree, find the toggle button for "show all files"
- [ ] Toggle ON — all files in the repo at that commit's tree are shown (not just changed files)
- [ ] Toggle OFF — only changed files are shown (default behavior)
- [ ] Toggle state resets when selecting a different commit
- [ ] Large repo with many files — verify no significant lag when toggling on

---

## Cross-cutting: Pass-through lane lines (current branch)
**Fix:** `112ded1` Render pass-through lanes beyond visible cull range

- [ ] Open a repo with a long-running feature branch (commits spread far apart on main)
- [ ] Scroll so the feature branch's commits are off-screen above — verify the vertical lane line still shows through the visible area
- [ ] Scroll back up — seamless connection, no gap or color mismatch at the seam
- [ ] Scroll rapidly up and down — no flicker or performance degradation
- [ ] With branch filter active — pass-through lines update when nodes change

---

## Smoke Tests

- [ ] Open Leaf fresh — repo loads without errors
- [ ] Switch between repositories — no leftover state from previous repo
- [ ] Commit, push, pull cycle works end-to-end
- [ ] GitFlow operations (start/finish feature/release/hotfix) still work
- [ ] Search for a commit by SHA — still works
- [ ] Working changes row appears when there are uncommitted changes

# Leaf Merge/Conflict Test Guide

## Overview

This guide covers 35 test repositories that exercise every merge, conflict, rebase, cherry-pick, and revert code path in Leaf. Each test has pass/fail criteria, known-fail labels for documented bugs, and git verification commands.

**Repos:** `C:\Users\Tim\Documents\Repos\LeafTestRepos\test-merge-NN-*\`
**Recreate:** `bash tests/create-merge-test-repos.sh`
**Manifest:** `LeafTestRepos\manifest.json`

## Result Classification

| Result | Meaning |
|--------|---------|
| PASS | All assertions met |
| KNOWN FAIL (KF-xx) | Failed as expected per known bug |
| UNEXPECTED FAIL | New regression |
| UNEXPECTED PASS | Known bug appears fixed |

## Known-Fail Labels

| Label | Bug | Description |
|-------|-----|-------------|
| KF-A1 | B1 | Abort uses `git merge --abort` for non-merge operations |
| KF-A2 | B2 | No cherry-pick abort implementation |
| KF-A3 | B3 | Revert throws on conflict, no recovery UI |
| KF-R1 | B4 | REVERT_HEAD not detected |
| KF-R2 | B5 | Rebase state not surfaced to ViewModels |
| KF-C1 | B6 | Cherry-pick conflict doesn't open resolution UI |
| KF-P1 | B7 | Stale leaf-merge-conflicts.txt |
| KF-V1 | B8 | No index validation before merge commit |
| KF-M1 | B10 | Conflict marker false positives |
| KF-S1 | B11 | Modal persists on repo switch |
| KF-H1 | B12 | Hunk staging desyncs conflict VM |
| KF-C2 | B13 | Revert/cherry-pick abort/continue commands exist but unused |

## Smoke Subset (8 repos for PR validation)

| # | Repo | Why |
|---|------|-----|
| 01 | `normal-merge-clean` | Happy path merge |
| 06 | `single-file-conflict` | Core conflict resolution |
| 08 | `multi-region-conflict` | Region navigation |
| 14 | `abort-merge` | Abort flow |
| 19 | `rebase-single-conflict` | Rebase flow |
| 24 | `cherry-pick-conflict` | Cherry-pick detection |
| 28 | `orphaned-conflicts` | Recovery flow |
| 34 | `repo-switch-during-conflict` | State cleanup |

## Debug Capture

All instrumented methods emit `[MERGE][STATE|OPS|UI|ERROR]` prefixed messages via `Debug.WriteLine`. To capture:

```
1. start_debug_capture(process: "Leaf")
2. Open repo / perform test actions
3. get_debug_messages() → filter for [MERGE]
4. stop_debug_capture()
```

---

## A. Merge Types — Clean State

### Test 01: Normal Merge (Clean)
**Repo:** `test-merge-01-normal-merge-clean`
**Category:** Resolution flow
**Initial State:** `main` branch, `feature/validation` exists with 2 commits (different files)
**Known Failures:** None

**Steps:**
1. Open repo in Leaf
2. Right-click `feature/validation` branch → Merge
3. Select "Normal Merge" → OK
4. [ ] Merge succeeds without conflicts
5. [ ] StatusMessage shows "Successfully merged feature/validation"
6. [ ] Graph shows merge commit with two parent lines
7. [ ] `feature/validation` branch label still exists

**Git Verification:**
```bash
git -C "$REPO" log -1 --format=%s  # "Merge branch 'feature/validation'"
git -C "$REPO" log -1 --format=%P | wc -w  # 2 (two parents)
```

---

### Test 02: Fast-Forward
**Repo:** `test-merge-02-fast-forward`
**Category:** Resolution flow
**Initial State:** `main` is direct ancestor of `feature/ahead` (2 commits ahead)

**Steps:**
1. Open repo in Leaf
2. Right-click `feature/ahead` → Merge
3. Select "Fast-Forward Only" → OK
4. [ ] Merge succeeds
5. [ ] No merge commit created (linear history)
6. [ ] `main` now points to same commit as `feature/ahead`

**Git Verification:**
```bash
git -C "$REPO" log --oneline -3  # Linear: Add line 3 → Add line 2 → Initial
```

---

### Test 03: Fast-Forward Not Possible
**Repo:** `test-merge-03-ff-not-possible`
**Category:** UI detection
**Initial State:** `main` and `feature/diverged` have diverged

**Steps:**
1. Open repo in Leaf
2. Right-click `feature/diverged` → Merge
3. Select "Fast-Forward Only" → OK
4. [ ] Error: "Cannot fast-forward: branches have diverged"
5. [ ] No merge commit created
6. [ ] Repository state unchanged

**Git Verification:**
```bash
git -C "$REPO" log --oneline -1  # Still "Main work"
```

---

### Test 04: Squash Merge
**Repo:** `test-merge-04-squash-merge`
**Category:** Resolution flow
**Initial State:** `feature/multi-commit` has 3 commits

**Steps:**
1. Open repo in Leaf
2. Right-click `feature/multi-commit` → Merge
3. Select "Squash Merge" → OK
4. [ ] Squash succeeds — staged changes appear in working changes
5. [ ] StatusMessage shows "Squash merged"
6. [ ] No automatic commit (user must commit manually)
7. [ ] Commit the squashed changes
8. [ ] Graph shows single new commit (not merge commit)

**Git Verification:**
```bash
git -C "$REPO" log -1 --format=%P | wc -w  # 1 (single parent, not merge)
git -C "$REPO" diff --stat HEAD~1  # Shows a.txt, b.txt, c.txt
```

---

### Test 05: Already Up To Date
**Repo:** `test-merge-05-already-up-to-date`
**Category:** UI detection
**Initial State:** `feature/done` already merged into `main`

**Steps:**
1. Open repo in Leaf
2. Right-click `feature/done` → Merge
3. Select "Normal Merge" → OK
4. [ ] StatusMessage indicates already merged or no-op
5. [ ] No new commit created

---

## B. Conflict Varieties

### Test 06: Single File Conflict
**Repo:** `test-merge-06-single-file-conflict`
**Category:** UI detection + Resolution flow
**Initial State:** Merge in progress, 1 file conflicted (Calculator.cs)
**Known Failures:** None (should pass)

**Steps:**
1. Open repo in Leaf
2. [ ] MergeStatusView shows "Merge in Progress"
3. [ ] Banner text includes "feature/validation"
4. [ ] Conflict count = 1
5. [ ] Approve button is DISABLED
6. Click "Resolve in Leaf"
7. [ ] ConflictResolutionView opens
8. [ ] Ours and Theirs panels show different Calculator.Add implementations
9. Click "Accept All Ours"
10. [ ] File moves to Resolved list
11. [ ] Approve button becomes ENABLED
12. Click "Approve"
13. [ ] MergeStatusView disappears
14. [ ] Graph shows merge commit with two parent lines

**Debug Messages:**
- `[MERGE][UI] LoadConflicts` — conflict list populated
- `[MERGE][UI] MarkResolved` — when file resolved
- `[MERGE][OPS] CompleteMerge` — when approved

**Git Verification:**
```bash
git -C "$REPO" ls-files -u  # empty
git -C "$REPO" log -1 --format=%s  # contains "Merge"
test ! -f "$REPO/.git/MERGE_HEAD"  # true
```

---

### Test 07: Multi-File Conflict
**Repo:** `test-merge-07-multi-file-conflict`
**Category:** Resolution flow
**Initial State:** 3 files conflicted (moduleA.cs, moduleB.cs, moduleC.cs)

**Steps:**
1. Open repo in Leaf
2. [ ] Conflict count = 3
3. Click "Resolve in Leaf"
4. [ ] All 3 files shown in Conflicted list
5. Resolve moduleA.cs (Accept Ours)
6. [ ] moduleA.cs moves to Resolved list, count = 2
7. Resolve moduleB.cs (Accept Theirs)
8. [ ] Count = 1
9. Resolve moduleC.cs (Accept Ours)
10. [ ] Approve button ENABLED
11. Approve
12. [ ] Clean state

---

### Test 08: Multi-Region Conflict
**Repo:** `test-merge-08-multi-region-conflict`
**Category:** Resolution flow
**Initial State:** 1 file (service.cs) with 3 conflict regions (MethodA, MethodB, MethodD)

**Steps:**
1. Open repo in Leaf, open conflict resolution
2. [ ] ConflictResolutionView shows service.cs
3. [ ] Multiple conflict regions visible (3 methods)
4. [ ] "Next Conflict Region" / "Previous Conflict Region" buttons navigate between regions
5. Accept Ours for region 1, Theirs for region 2, Ours for region 3
6. [ ] Mark Resolved becomes enabled
7. Mark Resolved → Approve
8. [ ] Clean state

---

### Test 09: Modify/Delete Conflict
**Repo:** `test-merge-09-modify-delete-conflict`
**Category:** UI detection
**Initial State:** target.cs modified on main, deleted on feature/delete

**Steps:**
1. Open repo in Leaf
2. [ ] MergeStatusView shows conflict
3. [ ] Conflict type is modify/delete (Theirs shows as deleted/empty)
4. Click "Resolve in Leaf"
5. [ ] Ours panel shows content, Theirs panel shows deletion
6. Accept Ours (keep the file) or Accept Theirs (delete it)
7. [ ] Resolution completes

---

### Test 10: Both-Added Conflict
**Repo:** `test-merge-10-both-added-conflict`
**Category:** UI detection
**Initial State:** newfile.cs created on both branches with different content

**Steps:**
1. Open repo in Leaf
2. [ ] Conflict shows add/add type
3. [ ] Base content is empty (no common ancestor for this file)
4. Resolve and complete

---

### Test 11: Rename + Content Conflict
**Repo:** `test-merge-11-rename-content-conflict`
**Category:** UI detection
**Initial State:** Both branches renamed original.cs → renamed.cs with different content

**Steps:**
1. Open repo in Leaf
2. [ ] MergeStatusView shows conflict in renamed.cs
3. Resolve conflict

---

### Test 12: Binary File Conflict
**Repo:** `test-merge-12-binary-file-conflict`
**Category:** UI detection
**Initial State:** Binary (image.png) + text (readme.txt) both conflicted

**Steps:**
1. Open repo in Leaf
2. [ ] Both files show as conflicted
3. [ ] Binary file (image.png) — Leaf should indicate binary conflict
4. [ ] Text file (readme.txt) — normal text conflict resolution
5. Resolve both (Use Ours/Theirs for binary)

---

### Test 13: Large Conflict (10 regions)
**Repo:** `test-merge-13-large-conflict`
**Category:** Resolution flow
**Initial State:** bigfile.cs with ~10 conflict regions

**Steps:**
1. Open repo in Leaf
2. [ ] ConflictResolutionView shows many conflict regions
3. [ ] Region navigation works (Next/Previous)
4. [ ] Performance acceptable (no lag with 10 regions)
5. Resolve all regions → Mark Resolved → Approve

---

## C. Abort/Cancel Flows

### Test 14: Abort Normal Merge
**Repo:** `test-merge-14-abort-merge`
**Category:** Abort/cleanup
**Initial State:** Merge in progress, 1 conflict

**Steps:**
1. Open repo in Leaf
2. [ ] MergeStatusView visible
3. Click "Abort" in MergeStatusView
4. [ ] MergeStatusView disappears
5. [ ] No `[MERGE][ERROR]` in debug messages
6. [ ] Repository is clean

**Git Verification:**
```bash
git -C "$REPO" status --porcelain  # clean
test ! -f "$REPO/.git/MERGE_HEAD"
```

---

### Test 15: Abort After Partial Resolve
**Repo:** `test-merge-15-abort-after-partial-resolve`
**Category:** Abort/cleanup
**Initial State:** 3 conflicts, a.cs already resolved (staged)

**Steps:**
1. Open repo in Leaf
2. [ ] Shows 2 unresolved, 1 resolved
3. Click Abort
4. [ ] ALL changes reverted (including the resolved file)
5. [ ] Clean state

**Git Verification:**
```bash
git -C "$REPO" status --porcelain  # clean
git -C "$REPO" diff --name-only --diff-filter=U | wc -l  # 0
```

---

### Test 16: Abort Cherry-Pick *(KNOWN FAIL: KF-A1, KF-A2)*
**Repo:** `test-merge-16-abort-cherry-pick`
**Category:** Abort/cleanup
**Initial State:** Cherry-pick in progress (CHERRY_PICK_HEAD exists)
**Known Failures:** KF-A1 (B1), KF-A2 (B2)

**Steps:**
1. Open repo in Leaf
2. [ ] MergeStatusView should show "Cherry-pick in Progress" *(may show "Merge in Progress" — KF)*
3. Click Abort
4. [ ] **EXPECTED KNOWN FAIL**: `git merge --abort` runs instead of `git cherry-pick --abort`
5. [ ] CHERRY_PICK_HEAD may NOT be cleaned up
6. [ ] Debug: `[MERGE][OPS] AbortMerge` instead of `AbortCherryPick`

**Git Verification (after expected fail):**
```bash
test -f "$REPO/.git/CHERRY_PICK_HEAD"  # true (still present = bug confirmed)
```

---

### Test 17: Squash Merge Conflict (No MERGE_HEAD)
**Repo:** `test-merge-17-squash-merge-conflict`
**Category:** Abort/cleanup
**Initial State:** Squash merge conflict — no MERGE_HEAD exists

**Steps:**
1. Open repo in Leaf
2. [ ] Conflict state detected even without MERGE_HEAD
3. [ ] Abort should use `git reset HEAD` or similar
4. [ ] Clean state after abort

---

### Test 18: Abort Revert *(KNOWN FAIL: KF-A1, KF-A3, KF-R1)*
**Repo:** `test-merge-18-abort-revert`
**Category:** Abort/cleanup
**Initial State:** Revert in progress (REVERT_HEAD exists)
**Known Failures:** KF-A1 (B1), KF-A3 (B3), KF-R1 (B4)

**Steps:**
1. Open repo in Leaf
2. [ ] **EXPECTED KNOWN FAIL**: Leaf does NOT detect REVERT_HEAD (B4)
3. [ ] No "Revert in Progress" banner (B4)
4. Try Abort
5. [ ] **EXPECTED KNOWN FAIL**: `git merge --abort` runs instead of `git revert --abort` (B1)
6. [ ] REVERT_HEAD still present

**Git Verification:**
```bash
test -f "$REPO/.git/REVERT_HEAD"  # true if B1/B4 confirmed
```

---

## D. Rebase Scenarios

### Test 19: Rebase Single Conflict
**Repo:** `test-merge-19-rebase-single-conflict`
**Category:** Resolution flow
**Initial State:** Rebase in progress, 1 conflict

**Steps:**
1. Open repo in Leaf
2. [ ] Rebase state detected
3. [ ] Conflict shown
4. Resolve conflict
5. [ ] Continue rebase
6. [ ] Rebase completes

**Git Verification:**
```bash
test ! -d "$REPO/.git/rebase-merge"
git -C "$REPO" log --oneline -3  # Linear history
```

---

### Test 20: Rebase Sequential Conflicts
**Repo:** `test-merge-20-rebase-sequential-conflicts`
**Category:** Resolution flow
**Initial State:** Rebase stopped at first of 2 conflicting commits

**Steps:**
1. Open repo in Leaf
2. Resolve first conflict → Continue
3. [ ] Second conflict appears
4. Resolve second conflict → Continue
5. [ ] Rebase completes

---

### Test 21: Rebase Abort *(KNOWN FAIL: KF-A1, KF-R2)*
**Repo:** `test-merge-21-rebase-abort`
**Category:** Abort/cleanup
**Initial State:** Rebase in progress
**Known Failures:** KF-A1 (B1), KF-R2 (B5)

**Steps:**
1. Open repo in Leaf
2. Click Abort
3. [ ] **EXPECTED KNOWN FAIL**: `git merge --abort` runs instead of `git rebase --abort` (B1)
4. [ ] Debug: `[MERGE][OPS] AbortMerge` instead of `AbortRebase`
5. [ ] Rebase state may persist

---

### Test 22: Rebase Skip
**Repo:** `test-merge-22-rebase-skip`
**Category:** Abort/cleanup
**Initial State:** Rebase stopped at conflicting commit, second commit is clean

**Steps:**
1. Open repo in Leaf
2. [ ] Skip button visible (rebase-specific)
3. Click Skip
4. [ ] Conflicting commit skipped, good commit applied
5. [ ] Rebase completes

---

### Test 23: Rebase Abort After Continue *(KNOWN FAIL: KF-A1, KF-R2)*
**Repo:** `test-merge-23-rebase-abort-after-continue`
**Category:** Abort/cleanup
**Initial State:** Rebase: first conflict resolved + continued, now at second conflict

**Steps:**
1. Open repo in Leaf
2. [ ] Second conflict visible
3. Click Abort (decide not to continue)
4. [ ] **EXPECTED KNOWN FAIL**: wrong abort command (B1, B5)

---

## E. Cherry-Pick & Revert

### Test 24: Cherry-Pick Conflict *(KNOWN FAIL: KF-C1)*
**Repo:** `test-merge-24-cherry-pick-conflict`
**Category:** UI detection
**Initial State:** Cherry-pick conflict (CHERRY_PICK_HEAD exists)
**Known Failures:** KF-C1 (B6)

**Steps:**
1. Open repo in Leaf
2. [ ] **EXPECTED KNOWN FAIL**: ConflictResolutionView does NOT open (B6)
3. [ ] Only status message "Cherry-pick has conflicts"
4. [ ] No merge banner or resolution UI

---

### Test 25: Cherry-Pick Clean
**Repo:** `test-merge-25-cherry-pick-clean`
**Category:** Resolution flow
**Initial State:** Ready to cherry-pick (feature/cp-clean has non-conflicting commit)

**Steps:**
1. Open repo in Leaf
2. Find the commit on feature/cp-clean → right-click → Cherry-pick
3. [ ] Cherry-pick succeeds
4. [ ] New commit created on main
5. [ ] StatusMessage: "Cherry-picked ..."

---

### Test 26: Revert Conflict *(KNOWN FAIL: KF-A3, KF-R1)*
**Repo:** `test-merge-26-revert-conflict`
**Category:** UI detection
**Initial State:** Revert conflict (REVERT_HEAD exists)
**Known Failures:** KF-A3 (B3), KF-R1 (B4)

**Steps:**
1. Open repo in Leaf
2. [ ] **EXPECTED KNOWN FAIL**: REVERT_HEAD not detected (B4)
3. [ ] No "Revert in Progress" UI
4. [ ] Exception may have been thrown during revert (B3)
5. Check debug output for `[MERGE][ERROR] RevertCommit`

---

### Test 27: Revert Clean
**Repo:** `test-merge-27-revert-clean`
**Category:** Resolution flow
**Initial State:** Last commit can be cleanly reverted

**Steps:**
1. Open repo in Leaf
2. Right-click last commit → Revert
3. [ ] Revert succeeds
4. [ ] New revert commit created

**Git Verification:**
```bash
git -C "$REPO" log -1 --format=%s  # "Revert "Add line (to revert)""
```

---

## F. Edge Cases

### Test 28: Orphaned Conflicts
**Repo:** `test-merge-28-orphaned-conflicts`
**Category:** Persistence/state
**Initial State:** Conflicts exist but MERGE_HEAD removed

**Steps:**
1. Open repo in Leaf
2. [ ] Leaf detects orphaned state (conflicts without MERGE_HEAD)
3. [ ] "Recovery Required" dialog appears
4. Choose "YES" (reset index only)
5. [ ] Conflicts cleared
6. [ ] Working directory changes preserved

**Git Verification:**
```bash
git -C "$REPO" diff --name-only --diff-filter=U | wc -l  # 0
```

---

### Test 29: Unrelated Histories
**Repo:** `test-merge-29-unrelated-histories`
**Category:** UI detection
**Initial State:** `unrelated` branch has no common ancestor with `main`

**Steps:**
1. Open repo in Leaf
2. Right-click `unrelated` → Merge
3. [ ] Error: unrelated histories
4. [ ] Prompt: "Do you want to merge anyway?"
5. Click Yes
6. [ ] Merge proceeds (may have conflicts or succeed)

---

### Test 30: Conflict Markers in Content *(KNOWN FAIL: KF-M1)*
**Repo:** `test-merge-30-conflict-markers-in-content`
**Category:** UI detection
**Initial State:** Merge conflict in parser.cs which legitimately contains `<<<<<<<` strings
**Known Failures:** KF-M1 (B10)

**Steps:**
1. Open repo in Leaf, open conflict resolution
2. Resolve the actual conflict (CountConflicts method)
3. [ ] **EXPECTED KNOWN FAIL**: "Mark Resolved" disabled because `ContainsConflictMarkers` sees `<<<<<<<` in source code (B10)

---

### Test 31: CRLF vs LF Conflict
**Repo:** `test-merge-31-crlf-lf-conflict`
**Category:** UI detection
**Initial State:** Merge conflict between CRLF and LF versions

**Steps:**
1. Open repo in Leaf
2. [ ] Conflict resolution shows the content
3. [ ] Line ending differences may appear as changes
4. Resolve and complete

---

## G. State & Persistence

### Test 32: Stale leaf-merge-conflicts.txt *(KNOWN FAIL: KF-P1)*
**Repo:** `test-merge-32-stale-leaf-merge-file`
**Category:** Persistence/state
**Initial State:** `.git/leaf-merge-conflicts.txt` contains old data + fresh merge conflict
**Known Failures:** KF-P1 (B7)

**Steps:**
1. Open repo in Leaf
2. [ ] Conflict resolution should show ONLY current conflict (file.cs)
3. [ ] Old file reference (old-file-from-previous-merge.cs) should NOT appear
4. Resolve and complete merge
5. [ ] **CHECK**: `test -f .git/leaf-merge-conflicts.txt` — should be deleted after merge
6. [ ] **EXPECTED KNOWN FAIL if file persists**: B7 confirmed

---

### Test 33: Close/Reopen During Merge *(KNOWN FAIL: KF-P1)*
**Repo:** `test-merge-33-reopen-during-merge`
**Category:** Persistence/state
**Initial State:** Merge in progress
**Known Failures:** KF-P1 (B7)

**Steps:**
1. Open repo in Leaf
2. [ ] MergeStatusView shows
3. Close Leaf completely
4. Reopen Leaf, re-add this repo
5. [ ] MergeStatusView should reappear (state persisted via MERGE_HEAD)
6. [ ] Conflict resolution still works
7. Complete merge
8. [ ] leaf-merge-conflicts.txt cleaned up (check for B7)

---

### Test 34: Repo Switch During Conflict *(KNOWN FAIL: KF-S1)*
**Repo A:** `test-merge-34-repo-switch-during-conflict-A` (in conflict)
**Repo B:** `test-merge-34-repo-switch-during-conflict-B` (clean)
**Category:** Persistence/state
**Known Failures:** KF-S1 (B11)

**Steps:**
1. Add both repos to Leaf
2. Select repo A
3. [ ] MergeStatusView shows for repo A
4. Click "Resolve in Leaf" to open ConflictResolutionView
5. Switch to repo B (click it in sidebar)
6. [ ] **EXPECTED KNOWN FAIL**: ConflictResolutionView modal may still be open with repo A data (B11)
7. [ ] Repo B should show clean state, no merge banner

**Additional (KF-H1 test):**
8. Switch back to repo A
9. In working changes view, try staging a hunk of the conflicted file
10. [ ] **EXPECTED KNOWN FAIL**: ConflictResolutionVM may desync from actual git state (B12)

---

### Test 35: Interactive Rebase (External)
**Repo:** `test-merge-35-interactive-rebase-external`
**Category:** UI detection
**Initial State:** Rebase in progress (rebase-merge directory)

**Steps:**
1. Open repo in Leaf
2. [ ] Leaf detects rebase-merge directory
3. [ ] Shows rebase-related UI (or at minimum doesn't corrupt state)
4. [ ] Abort should use `git rebase --abort`

---

## Operation Type Detection Summary

| Operation | Sentinel | Expected UI Label | Expected Abort | Test Repos |
|-----------|----------|-------------------|----------------|------------|
| Merge | `.git/MERGE_HEAD` | "Merge in Progress" | `git merge --abort` | 06, 14 |
| Cherry-pick | `.git/CHERRY_PICK_HEAD` | "Cherry-pick in Progress" | `git cherry-pick --abort` | 16, 24 |
| Rebase | `.git/rebase-merge/` | "Rebase in Progress" | `git rebase --abort` | 19, 21 |
| Revert | `.git/REVERT_HEAD` | "Revert in Progress" | `git revert --abort` | 18, 26 |
| Orphaned | No sentinel, unmerged index | "Recovery Required" | `git reset HEAD` | 28 |

## Stagehand Assertions

### Positive (per category)

**UI Detection:**
- `survey_scene` → MergeStatusView visible
- Text contains expected operation label
- Conflict count matches expected
- `get_debug_messages` → `[MERGE][STATE]` shows correct sentinels

**Resolution Flow:**
- File moves from Conflicted → Resolved list
- Approve button enabled only when all resolved
- After complete: MergeStatusView gone, graph updated
- `get_debug_messages` → `[MERGE][UI] MarkResolved`, `[MERGE][OPS] CompleteMerge`

**Abort:**
- MergeStatusView disappears after abort
- No `[MERGE][ERROR]` in debug (except known-fail repos)
- No stale conflict UI

### Negative
- Wrong operation controls NOT visible
- Stale conflict list NOT present after abort/switch
- Approve button NOT enabled while unresolved conflicts remain
- `[MERGE][ERROR]` NOT present for non-known-fail repos

### Button State Checks

| Button | Condition | Repos |
|--------|-----------|-------|
| Approve | Disabled until all resolved | 06, 07, 08, 13 |
| Approve | Enabled when all resolved | 06 (after resolving) |
| Abort | Always enabled during merge/rebase | 06, 14, 19 |
| Skip | Only visible during rebase | 22 (visible), 06 (NOT visible) |
| Resolve in Leaf | Enabled when conflict selected | 06, 07 |

## Git Index Verification (Bash)

After resolution + complete:
```bash
git -C "$REPO" ls-files -u              # empty (no unmerged)
git -C "$REPO" log -1 --format=%B       # merge message
test ! -f "$REPO/.git/MERGE_HEAD"       # no sentinel
test ! -f "$REPO/.git/leaf-merge-conflicts.txt"  # cleanup check
```

After abort:
```bash
git -C "$REPO" status --porcelain       # clean
test ! -f "$REPO/.git/MERGE_HEAD"
test ! -f "$REPO/.git/CHERRY_PICK_HEAD"
test ! -d "$REPO/.git/rebase-merge"
test ! -f "$REPO/.git/REVERT_HEAD"
```

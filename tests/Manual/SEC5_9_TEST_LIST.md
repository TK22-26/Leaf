# §5.9 Customizable Keyboard Shortcuts — Manual Test List

Walk through these. Check the box as you go. Estimated time: **~20 minutes**.

## 0. Prerequisites

- [ ] **Build is fresh:** `dotnet build Leaf.sln` shows 0 errors, 0 warnings (other than the pre-existing 2 `CS0067` test-project warnings).
- [ ] **Unit tests green:** `dotnet test tests/Leaf.Tests/Leaf.Tests.csproj --no-build` shows 819 / 819 pass.
- [ ] **Automated harness:** `powershell -ExecutionPolicy Bypass -File tests/Manual/Test-Shortcuts.ps1` shows 7 / 7 pass.
- [ ] **Test repo present** at `C:/Users/Tim/Documents/Repos/LeafTestRepos/merge-overhaul-test/repo`.

Run Leaf for the rest of the tests:
```
src/Leaf/bin/Debug/net10.0-windows/Leaf.exe --repo C:/Users/Tim/Documents/Repos/LeafTestRepos/merge-overhaul-test/repo
```

---

## 1. App-scope shortcuts fire (Phase 1 + 2 wiring)

Click somewhere in the graph area first so a TextBox isn't capturing keys.

- [ ] **Ctrl+`** (backtick) → integrated terminal toggles open / closed
- [ ] **Ctrl+Shift+P** → command palette appears (VS Code convention)
- [ ] **Double-tap Space** (within 300 ms) → also opens command palette (separate handler in `MainWindow.PreviewKeyDown`, not in the registry)
- [ ] **F1** → "Report an Issue" dialog opens (close it after)
- [ ] **F5** → status shows "Fetching all repositories…" briefly (no error toast)
- [ ] **Ctrl+B** → new-branch popup appears in the branch list
- [ ] **Ctrl+Shift+K** → push attempts (no remote, will silently no-op or show "no remote")
- [ ] **Ctrl+Shift+L** → pull attempts (same — no remote on test repo)
- [ ] **Ctrl+Alt+S** → stash dialog or status (test repo has no changes, may toast "nothing to stash")

> **Note:** `Alt+Space` is reserved by Windows for the system menu and may
> not reach the InputBindings on all systems. The command palette uses
> `Ctrl+Shift+P` as the primary chord; the double-tap-Space alias is
> always available as a fallback.

If any chord doesn't fire: focus may be in a TextBox. Click on the commit graph first.

---

## 2. Settings → Keyboard Shortcuts panel (Phase 3 UI)

- [ ] **Open Settings:** File → Settings, dialog opens
- [ ] **Tree node visible:** "Keyboard Shortcuts" appears in the left tree (with a Keyboard icon)
- [ ] **Click it:** the right pane shows:
  - Title: "Keyboard Shortcuts"
  - Subtitle: explanatory text about Edit / press / Enter
  - Toolbar row: **Search** field on left, **Reset all to defaults** button on right
  - Categorized rows below

### 2a. Layout / categories

The list should be grouped, with section headers in this order:

- [ ] **View** — 3 rows (Toggle terminal, Toggle command palette, Report an issue)
- [ ] **Repository** — 4 rows (Fetch, Pull, Push, Refresh)
- [ ] **Branch** — 2 rows (Create, Checkout)
- [ ] **Commit** — 3 rows (Commit, Stash, Pop stash)
- [ ] **Merge editor** — 15 rows

### 2b. Per-row layout

Pick any row (e.g. "Toggle integrated terminal"):

- [ ] **Label:** "Toggle integrated terminal"
- [ ] **Command id** below in small monospace: `view.toggleTerminal`
- [ ] **Gesture pill** showing the current binding (e.g. `Ctrl+Oem3` for backtick)
- [ ] **Edit** button on the right
- [ ] **Reset** (↻ icon) button on the far right

### 2c. Search filter

- [ ] Type **`fetch`** in the search box → only "Fetch from remote" row remains visible
- [ ] Type **`merge`** → all 15 Merge-editor rows show, others hidden
- [ ] Clear the search → all rows return

### 2d. Edit / capture / save flow

Pick "Toggle integrated terminal":

- [ ] Click **Edit** → button text changes to "Save", capture box appears with placeholder "Press a key combination…"
- [ ] Press **Ctrl+Shift+T** → capture box updates to show `Ctrl+Shift+T`
- [ ] Press **Esc** → capture cancels, gesture pill returns to original `Ctrl+Oem3`
- [ ] Click **Edit** again
- [ ] Press **Ctrl+Alt+T** → capture shows `Ctrl+Alt+T`
- [ ] Press **Enter** OR click **Save** → row exits edit mode, gesture pill now shows `Ctrl+Alt+T`
- [ ] Close the Settings dialog
- [ ] Press **Ctrl+`** → terminal does **NOT** toggle (binding moved away)
- [ ] Press **Ctrl+Alt+T** → terminal DOES toggle (new binding active)
- [ ] Re-open Settings → "Toggle integrated terminal" row still shows `Ctrl+Alt+T`

### 2e. Reset individual

- [ ] Click the ↻ Reset button on the "Toggle integrated terminal" row
- [ ] Pill reverts to default (`Ctrl+Oem3`)
- [ ] Close + re-open Settings → still shows default
- [ ] **Ctrl+`** toggles terminal again

### 2f. Conflict warning

- [ ] Click Edit on "Toggle integrated terminal"
- [ ] Press **Alt+1** (which is bound to "Accept current — Ours" in merge scope)
- [ ] Capture shows `Alt+D1` and **NO** conflict warning (different scope = OK)
- [ ] Press Esc to cancel
- [ ] Click Edit on **"Pull from remote"**
- [ ] Press **Ctrl+`** (the same gesture used by Toggle terminal — same scope!)
- [ ] **Conflict warning appears in amber:** `"Already used by 'view.toggleTerminal'. Saving will reassign."`
- [ ] Press Esc to cancel — no save happens

### 2g. Reset all to defaults

- [ ] Apply 2-3 custom overrides on different rows (any combination)
- [ ] Click **Reset all to defaults** at the top
- [ ] All rows snap back to their default gestures
- [ ] Close + re-open Leaf → all defaults still in place

---

## 3. Persistence (already automated — but verify visually)

- [ ] Open Settings → set "Toggle integrated terminal" to `Ctrl+Alt+T`, Save
- [ ] Close Leaf entirely
- [ ] Re-launch Leaf
- [ ] Open Settings → row still shows `Ctrl+Alt+T`
- [ ] **Ctrl+Alt+T** still toggles the terminal
- [ ] Reset that row before continuing

---

## 4. Merge editor scope (Phase 2 migration)

Set up a conflict if you don't have one. From a terminal in the test repo:
```bash
git checkout main
git checkout -b conflict-test
echo "ours version" >> README.md
git commit -am "ours change"
git checkout main
echo "theirs version" >> README.md
git commit -am "theirs change"
git merge conflict-test    # creates a conflict
```

Then in Leaf, click the conflicted file to open the merge editor.

- [ ] **Alt+1** — accepts Ours for the current conflict
- [ ] Undo (Ctrl+Z), then **Alt+2** — accepts Theirs
- [ ] Undo, then **Alt+3** — accepts Both
- [ ] **F8** — jumps to the next conflict (or stays on current if only one)
- [ ] **Shift+F8** — jumps backward
- [ ] **Alt+Right** / **Alt+Left** — moves change-span cursor within the conflict
- [ ] **Alt+Down** / **Alt+Up** — jumps to next/prev auto-merged region
- [ ] **Ctrl+Z** / **Ctrl+Y** — undo / redo
- [ ] **Ctrl+Shift+Z** — also redoes (the hardcoded XAML alias preserved)
- [ ] **Ctrl+K** — opens the merge command palette
  - [ ] Each entry shows the command's **current** keybinding (e.g. "Alt+1" next to "Accept Ours")
  - [ ] After rebinding any merge shortcut in Settings → re-open palette → palette text reflects the new binding
- [ ] **Ctrl+Enter** — marks current file resolved
- [ ] **Alt+B** — blame peek popover appears for current line
- [ ] **Alt+A** — AI resolution request fires (no-op if AI not configured; check log for `[InteractiveRebase]` ... actually `[Merge]` entries)

Abort the merge after testing: `git merge --abort`, then `git branch -D conflict-test`.

---

## 5. Edge cases

- [ ] **Hand-edit `%APPDATA%\Leaf\settings.json`** — set `shortcutOverrides.view.toggleTerminal` to `"garbage-not-a-gesture"`. Restart Leaf. Settings panel shows the row (default gesture, since the parse failed). Look for a `[Shortcuts]` warning in `%LOCALAPPDATA%\Leaf\leaf.log`.
- [ ] **Hand-edit settings.json** — set `view.toggleTerminal` to `""` (empty). Restart. Settings panel row shows "(unbound)". Pressing `Ctrl+`` does nothing. Click Reset on the row → default returns.
- [ ] **Rename test:** add `"some.future.id": "Ctrl+X"` to the JSON. Restart. Leaf launches without error. Log shows `[Shortcuts] Override for unknown id 'some.future.id' ignored`.
- [ ] **Multiple Leaf instances:** launch a second Leaf with the same repo. Both should read the same settings file (no concurrency assertion failures).

---

## 6. Smoke regressions (verify §5.5 still works)

The shortcut migration touched `MergeEditorView.xaml`. Sanity-check the interactive-rebase feature is still happy:

- [ ] Right-click a non-root commit → "Rebase Interactively from Here…" → window opens
- [ ] Cancel — no errors

---

## Final summary

If every box above is checked: **§5.9 ships.** Merge `feat/customizable-shortcuts` → `develop`.

If anything fails:
- Note the section + specific item
- Capture `%LOCALAPPDATA%\Leaf\leaf.log` lines around the failure (search for `[Shortcuts]`)
- Open the Settings panel and screenshot the row in question

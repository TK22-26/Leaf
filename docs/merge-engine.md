# Leaf Merge Engine Architecture

This document is the permanent record of the Leaf three-way merge engine as
it exists after the six-phase rewrite (plan §8, phases 1–6). It is intended
for future maintainers — the person fixing a bug in the word-diff three
months from now, the person swapping the AI backend for a different provider
next year, the person asking "why did we vendor AvalonEdit?" in 2028.

If any of the behaviour described here disagrees with the code, the code is
right and this document is stale — file an issue.

---

## 1. Why this exists

Before the rewrite, Leaf's merge editor was a 1 125-line `ConflictResolutionViewModel`
driving a custom line-by-line Myers-diff three-way merge implemented in 384
lines of `ThreeWayMergeService`. The `MergeRegion` enum model couldn't express
"accept both sides, theirs first, with smart deduplication", the line-level
output was coarser than Git's own, and the UI ran on `AvalonEdit` with
virtualisation quirks that made pane-to-pane connection lines impossible.

The rewrite makes three architectural bets:

1. **Don't reimplement merging.** Shell out to `git merge-file --diff-algorithm=histogram --zdiff3`.
   Byte-identical to `git merge` on the command line; zero discrepancy risk.
2. **Port VS Code's `ModifiedBaseRange` data model.** First-class "accept both,
   order-preserved, smart-combined" resolution — the one thing every other
   OSS merge UI gets wrong.
3. **Vendor AvalonEdit into the Leaf repo under its MIT licence.** Strip the
   modules we don't need; expose the virtualisation primitives we do.

The result is a merge editor that is genuinely competitive with Beyond
Compare, Sublime Merge, and VS Code — and doesn't depend on any NuGet
package that could drift out from under us.

---

## 2. The six phases, at a glance

| Phase | What landed | Most important file |
|---|---|---|
| 1 | Algorithm pipeline + data model (no visible UI change) | `Services/Merge/GitMergeFileEngine.cs` |
| 2a | Vendored AvalonEdit source import under `Leaf.TextEdit` | `src/Leaf/TextEdit/` |
| 2b | Stripped unused modules; exposed virtualisation primitives | `src/Leaf/TextEdit/MergePaneGlyphLayout.cs` |
| 2c | New view foundation: `MergeEditorView`, `ReadOnlyMergePane`, `ResultPane` | `Views/Merge/MergeEditorView.xaml` |
| 3 | Word-level highlighting + minimap heat strip | `Services/Merge/WordDiffService.cs` |
| 4 | Meld-style bezier connection lines + keyboard shortcuts + scroll sync | `Controls/Merge/PaneConnectionCanvas.cs` |
| 5 | AI-assisted resolution via MCP (provider-agnostic, opt-in) | `Services/Merge/McpMergeAssistant.cs` |
| 6 | Image-conflict rendering: side-by-side / onion / swipe / diff / overlay | `Controls/Merge/ImageConflictPane.cs` |

---

## 3. End-to-end request flow

A user hits a merge conflict. Here's what happens, call by call:

```
MainViewModel.OnMergeStateChangedAsync
  └─ new MergeEditorViewModel(gitService, clipboardService,
                              mergeEngine, wordDiffService,
                              aiAssistant?, imageService?, repoPath)
      └─ LoadConflictsAsync
          └─ IGitService.GetConflictsAsync
              → populates ConflictInfo[] with base/ours/theirs content
          └─ SelectedConflict = first unresolved
              └─ BuildDocumentForSelectedAsync      (partial func)
                  ├─ if (IsBinaryContent) → ImageMergeService.Load + return
                  └─ IMergeEngine.MergeAsync(filePath, base, ours, theirs)
                      └─ GitMergeFileEngine (shell-out)
                          ├─ write stages to temp dir
                          ├─ git -c core.autocrlf=false merge-file
                          │       --diff-algorithm=histogram --zdiff3 -p …
                          ├─ ConflictMarkerParser (two-pass zdiff3)
                          └─ return MergeDocument { Ranges, BaseLines, … }
              └─ ComputeWordDiffsAsync (Task.Run)
                  └─ WordDiffService.DiffLines pairs ours/theirs lines
              └─ RangeStatesChanged? → ReadOnlyMergePane.InvalidateVisual
```

Resolution is a series of VM command invocations:

```
User clicks an ours-side checkbox →
  MergeEditorView.xaml.cs.ApplyCheckbox →
    MergeEditorViewModel.AcceptOursCommand.Execute(rangeIndex) →
      RangeStates[idx] = ResolutionState.AcceptOurs.Instance →
      PushUndo → RangeStatesChanged
                      └→ ReadOnlyMergePane.InvalidateVisual
                      └→ VM.ComposedText recomputed
                          (MergeDocument.ComposeResolvedText)
                      └→ ResultPane re-renders bound text
```

Mark-resolved writes to disk and stages:

```
MergeEditorViewModel.MarkResolvedAsync:
  1. ComposedText = ComposeResolvedText(RangeStates)
  2. Commit gate: ContainsConflictMarkers(ComposedText) === false
  3. File.WriteAllTextAsync(fullPath, ComposedText)
  4. IGitService.MarkConflictResolvedAsync (git add)
  5. AutoAdvance to next unresolved
```

---

## 4. Core data model (Phase 1)

### `MergeDocument` — `Models/Merge/MergeDocument.cs`

Immutable per-file snapshot. Holds input texts, the initial merged output
(with zdiff3 markers still present for unresolved regions), the full list
of `ModifiedBaseRange`s, sniffed line-ending, and trailing-newline flag.

`ComposeResolvedText(Dictionary<int, ResolutionState>)` is the only mutator
the UI ever calls on this object. It walks the initial-merged lines in
order, substituting resolved content for ranges that have a state in the
dictionary and preserving the zdiff3 markers for ranges that don't. This
function is the commit-gate's source of truth.

### `ModifiedBaseRange` — `Models/Merge/ModifiedBaseRange.cs`

One conflict region. Carries:

- `Index` — stable numeric key; used by `RangeStates`
- `Base`, `Ours`, `Theirs` — `LineRange`s in the respective inputs
- `ResultMarkedRange` — `LineRange` of the region's opener/separator/closer
  in the initial-merged text
- `BaseLines`, `OursLines`, `TheirsLines` — per-side line arrays
- `OursDiffs`, `TheirsDiffs` — detailed per-side mappings (reserved for
  future sub-line precision; presently empty for engine-produced ranges)
- `IsConflicting` — false for auto-merged hunks that are shown for
  context but don't require user action
- `IsOrderRelevant` — whether "AcceptBoth, ours-first" vs "AcceptBoth,
  theirs-first" could produce different output
- `OursLabel`, `BaseLabel`, `TheirsLabel` — captured from zdiff3 marker
  lines (e.g. `<<<<<<< HEAD`, `||||||| base-sha`, `>>>>>>> branch-name`)

### `ResolutionState` — discriminated union

```csharp
public abstract record ResolutionState
{
    public sealed record Unresolved : ResolutionState
    {
        public static readonly Unresolved Instance = new();
    }
    public sealed record AcceptOurs : ResolutionState
    {
        public static readonly AcceptOurs Instance = new();
    }
    public sealed record AcceptTheirs : ResolutionState
    {
        public static readonly AcceptTheirs Instance = new();
    }
    public sealed record AcceptBoth(bool FirstOurs, bool SmartCombine) : ResolutionState;
    public sealed record Manual(string Text) : ResolutionState;
}
```

`AcceptBoth(FirstOurs, SmartCombine)`:

- `FirstOurs = true` prepends the ours block, then theirs
- `SmartCombine = true` dedupes lines that appear in both sides exactly
- `SmartCombine = false` concatenates blindly (the "dumb combine" right-click escape)

---

## 5. The merge pipeline (Phase 1)

### `GitMergeFileEngine` — `Services/Merge/GitMergeFileEngine.cs`

Responsible for converting `(base, ours, theirs)` triples into a
`MergeDocument`. The hot path:

1. **Temp dir**: `%TEMP%\leaf-merge-{guid}\`, cleaned up in `finally` with
   exponential-backoff retry (AV scanners briefly hold file handles on
   Windows, so the retry prevents spurious cleanup failures from tearing
   down the merge). Always scoped to one operation.
2. **Encoding**: explicit `UTF8Encoding(encoderShouldEmitUTF8Identifier: false)`
   on every stream. `core.autocrlf=false` is passed on the git invocation
   itself so Git doesn't silently rewrite line endings during the read.
3. **Line-ending preservation**: sniffs the working-tree file's original
   line endings before invoking git. zdiff3 always emits `\n`; we
   re-convert on the way back out when composing resolved text.
4. **Invocation**: `git -c core.autocrlf=false merge-file --diff-algorithm=histogram --zdiff3 -p
   <ours-temp> <base-temp> <theirs-temp>`. Stdout is the combined merged
   output; exit code is conflict count.
5. **Parse**: `ConflictMarkerParser` walks the output line by line in two
   passes. Pass 1 locates triads; pass 2 resolves "lookalike" markers
   (source code that happens to contain `<<<<<<<` in a comment). When in
   doubt — e.g. a `<<<<<<<` with no matching `=======` and `>>>>>>>` in
   the next ~40 lines — it treats the marker as content.

The engine never caches. `MergeAsync` is called per file selection and is
deterministic for a given input triple.

### `ConflictMarkerParser` — `Services/Merge/ConflictMarkerParser.cs`

zdiff3 format is:

```
<<<<<<< ours-label
<ours lines>
||||||| base-label
<base lines>
=======
<theirs lines>
>>>>>>> theirs-label
```

The parser captures the three labels per triad and stores them on the
resulting `ModifiedBaseRange`. This drove the fix for issue C-4 — Leaf
used to hard-code "HEAD"/"base"/"incoming" which was wrong for
cherry-picks, rebases, and three-way merges involving detached HEADs.

### Commit gate — `MergeEditorViewModel.ContainsConflictMarkers`

Final defence before writing a file. Returns `true` iff the content
contains a full zdiff3 structural triad in order:

1. A line starting with `<<<<<<<` (seven angle brackets)
2. Somewhere after it, a line equal to exactly `=======` (seven equals)
3. Somewhere after that, a line starting with `>>>>>>>` (seven close-angles)

A `<<<<<<<` in prose documentation without the matching separator + closer
is *not* treated as unresolved state (documentation that talks about
conflict markers is a thing). CRLF-tolerant: strips a trailing `\r` from
each scanned line.

---

## 6. Vendored AvalonEdit (Phase 2a/2b)

Under `src/Leaf/TextEdit/`, namespace `Leaf.TextEdit`. MIT licence
attribution in `LICENSE-AvalonEdit.txt` at the repo root.

Stripped on the way in:

- `TextEdit/Search/` — AvalonEdit's built-in search dialog
- `TextEdit/Folding/` — no folding in the merge view
- `TextEdit/CodeCompletion/` — no completion
- `TextEdit/Snippets/` — no snippets
- Any standalone XAML dialogs/windows beyond `TextEditor`

Added on the way out:

- Virtualisation-aware public coordinate primitives on `TextView`:
  `GetVisualTop(int lineIndex)`, `DefaultLineHeight`, `TotalContentHeight`,
  `LineHitTest(double y)` — all return valid values for off-screen lines.
  These are what the `PaneConnectionCanvas` and `ConflictMinimap` sample.
- `MergePaneGlyphLayout` — single source of truth for font family, size,
  line height, tab stops, indent guides. Both `ReadOnlyMergePane`
  (custom) and the vendored `TextEditor` (Result pane) consume it, so
  alignment across panes is perfect by construction.

### Upkeep

Bug-fix cherry-picks from upstream AvalonEdit are on a best-effort basis.
The vendoring decision accepted "we own the code now; future upstream
drift is not our problem". In practice, the code has barely moved
since 2023, so this is close to free.

---

## 7. UI foundation (Phase 2c)

### `MergeEditorView` — `Views/Merge/MergeEditorView.xaml`

Top-level Window. Layout:

```
┌─────────────────────────────────────────────────────────────┐
│ Header (source → target branch, resolved/total counter)     │
├──────────┬──────────────────────────────────────────────────┤
│ File     │ ┌──────────────┬─┬──────────────┐                │
│ list     │ │  OURS (custom)│c│ THEIRS (cust)│                │
│          │ ├──────────────┴─┴──────────────┤                │
│          │ │       RESULT  (vendored)      │                │
│          │ └──────────────────────────────────┘             │
├──────────┴──────────────────────────────────────────────────┤
│ Footer (Undo/Redo | AcceptAllOurs/Theirs | Mark | Complete) │
└─────────────────────────────────────────────────────────────┘
```

The middle "c" column (40 px) is the `PaneConnectionCanvas` strip. Two-row
sub-grid (50/50) hosts input panes on top, result pane on bottom; the
canvas only spans the top row.

### `ReadOnlyMergePane` — `Controls/Merge/ReadOnlyMergePane.cs`

Custom `FrameworkElement` with `IScrollInfo`. Inputs: text lines,
conflict regions, resolution state, word-diff segments.

Draws in one `OnRender` pipeline:

1. Region backgrounds (ours-tint / theirs-tint)
2. Word-level highlights via `FormattedText.BuildHighlightGeometry`
   (pixel-accurate glyph rectangles — handles tabs, variable-width fonts,
   RTL correctly)
3. Line text via `FormattedText`
4. Line numbers (direct draw, not a margin control)
5. Accept-checkbox glyphs per region

`DrawingVisual` caching per viewport tile; virtualised by default.

### `ResultPane` — `Controls/Merge/ResultPane.cs`

Thin wrapper around `Leaf.TextEdit.TextEditor`. As of Phase 2c:
`IsReadOnly = true`, `Focusable = false`. Manual edits in the Result
pane will be reintroduced in a future phase with per-range text
mapping so only the touched range becomes `Manual`. The current
`OnResultTextChanged` handler throws `NotImplementedException` — a
hard block so a future developer flipping `IsReadOnly` without
implementing the routing gets an immediate failure instead of silent
whole-buffer-to-Ranges[0] corruption.

### Checkbox interaction model

Each conflict region renders two checkboxes, one in each input pane.
Click an ours-side checkbox to:

- If theirs-side is not accepted → `AcceptOurs`
- If theirs-side is already accepted → `AcceptBoth(firstOurs=true)`

Click a theirs-side checkbox: symmetric. Click order is captured so
"accepted theirs first, then ours" produces `AcceptBoth(firstOurs=false)`.
Right-click → "Dumb combine" flips `SmartCombine` to `false` on an
existing `AcceptBoth`.

---

## 8. Word-level highlighting + minimap (Phase 3)

### `WordDiffService` — `Services/Merge/WordDiffService.cs`

Per-conflict-range, pairs ours-line-i with theirs-line-i (extra lines
on whichever side is longer are emitted as pure adds). Inside each
pair, runs a secondary Myers diff at token granularity via a custom
`IChunker` that splits on Unicode whitespace + punctuation
(`--word-diff-regex=[A-Za-z_][A-Za-z_0-9]*` equivalent).

Returns `TokenLine[]` per side — fed directly into
`ReadOnlyMergePane.OnRender` so unchanged tokens get a dim background
and changed tokens get the full highlight colour.

Results are computed on a background thread (`Task.Run`) because a
large conflict block (e.g. a regenerated package-lock.json with 1 000+
lines) would otherwise freeze the UI for ~1 s per file select.

### `ConflictMinimap` — `Controls/Merge/ConflictMinimap.cs`

12-px vertical strip right of each pane. Renders a heat-strip of the
document: grey for unchanged, side colour for changed, red for
unresolved conflicts, green for resolved. Click to jump, drag to scroll.
Consistent with the `GitGraphCanvas` `DrawingVisual` caching pattern.

---

## 9. Connection lines + keyboard (Phase 4)

### `PaneConnectionCanvas` — `Controls/Merge/PaneConnectionCanvas.cs`

WPF `Canvas` overlay between the input panes. Renders bezier curves
from each conflict's ours-side line to its theirs-side line. Colour-coded
by resolution state: grey (unresolved), blue (ours), green (theirs),
gradient (both). Hidden for auto-merged (non-conflicting) ranges.

Coordinate mapping samples `MergePaneGlyphLayout.GetVisualTop(pane, line)`.
Because Phase 2b exposed that as virtualisation-aware, off-screen
conflicts still anchor correctly and clip cleanly at the pane edges.

### Keyboard shortcuts

Declared on the view in `InputBindings`:

| Shortcut | Command |
|---|---|
| `Alt+1` | Accept current conflict — ours |
| `Alt+2` | Accept current conflict — theirs |
| `Alt+3` | Accept current conflict — both |
| `F8` | Next conflict (skips resolved) |
| `Shift+F8` | Previous conflict |
| `Ctrl+Enter` | Mark file resolved + advance |
| `Ctrl+Z` | Undo last resolution |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo |
| `Alt+A` | Ask AI (Phase 5) |

"Current conflict" is tracked by `CurrentConflictIndex` on the VM —
auto-advanced on file select, advanced by F8.

### Scroll sync

Mirrored via `MergeEditorView.xaml.cs.OnOursScrollChanged` /
`OnTheirsScrollChanged`. Flag `_suppressScrollSync` prevents re-entrant
ping-ponging when the mirrored scroll fires its own `ScrollChanged`.

---

## 10. AI-assisted resolution (Phase 5)

The WPF client never talks to a model API directly. Every request goes
through an external **MCP (Model Context Protocol) server** over
stdio JSON. This mirrors the pattern used by Stagehand, Rosy, and
Mix-of-Experts — provider-agnostic by construction.

### `IAiMergeAssistant` — `Services/Merge/IAiMergeAssistant.cs`

```csharp
public interface IAiMergeAssistant
{
    bool IsEnabled { get; }
    bool IsConsentGiven { get; }
    string? McpServerPath { get; }

    Task<AiResolution?> RequestResolutionAsync(
        AiResolutionRequest request,
        CancellationToken cancellationToken = default);
}
```

### `McpMergeAssistant` — `Services/Merge/McpMergeAssistant.cs`

Reference implementation. Per request:

1. Silently return `null` when `!IsEnabled || !IsConsentGiven`
2. Throw `AiMergeAssistantException` when no server path is configured
   or the path doesn't exist on disk
3. Spawn a fresh `Process` with `UseShellExecute=false`,
   `CreateNoWindow=true`, UTF-8 no-BOM on every stream
4. Wrap `Win32Exception`, `InvalidOperationException`,
   `PlatformNotSupportedException` from `Process.Start` so the VM's
   `AiError` event fires instead of a generic toast
5. Pre-start both stdout and stderr readers BEFORE writing stdin (pipe-buffer
   deadlock prevention — mirrors `GitCommandRunner`)
6. `ObserveFaults(task)` on both readers so they can never become
   `TaskScheduler.UnobservedTaskException`
7. Cancellation: `cancellationToken.Register` calls `process.Kill(entireProcessTree: true)`
8. Log `outcome=success|error|cancelled duration_ms=X` — timing + outcome
   only, never request/response content

### Wire protocol

Request — one JSON object on stdin:

```json
{
    "tool": "resolve_conflict",
    "filePath": "src/Example.cs",
    "language": "csharp",
    "baseLines":    ["…"],
    "oursLines":    ["…"],
    "theirsLines":  ["…"],
    "contextBefore": ["…"],
    "contextAfter":  ["…"]
}
```

Response — one JSON object on stdout:

```json
{
    "proposedText": "…",
    "rationale": "short explanation",
    "confidence": "high" | "medium" | "low"
}
```

`tools/leaf-merge-mcp/README.md` has the full contract.

### Privacy contract

Payload restricted to:

- `{base, ours, theirs}` line arrays from the conflict region itself
- `filePath` — repo-relative, not absolute
- `language` — inferred from extension
- `contextBefore`/`contextAfter` — default 20 lines each side, hard-capped
  at 200 via `CapContext` (enforced even if the default becomes configurable)

Never sent: branch names, commit messages, full-file contents, other
files, author identity, repository state.

### Consent flow

1. User clicks "Ask AI" for the first time in a session
2. `MergeEditorViewModel.RequestAiResolution` checks
   `_aiAssistant.IsConsentGiven`; if false, fires `AiConsentRequested`
3. `MergeEditorView.xaml.cs.OnAiConsentRequested` shows
   `AiConsentDialog` (modal, owner = merge editor)
4. On accept: view persists `AiMergeConsentGiven = true` and
   `AiMergeEnabled = true` in settings, then calls
   `vm.ResumeAiRequestAfterConsent()`
5. The re-fired request passes the now-true consent gate and the MCP
   call proceeds
6. Resettable from Settings → AI Integrations → Merge Assistant

### Proposal presentation

Server response fires `AiResolutionReceived`; view shows
`AiResolutionDialog`. The proposed text is pre-populated in an editable
textbox (the AI is a draftsman, not a source of truth — user edits
before accepting). Accept becomes `ResolutionState.Manual(text)` on the
current range; downstream composition + commit gate + undo work without
any AI-specific code paths.

---

## 11. Image conflicts (Phase 6)

### `GitCliHelpers.ReadConflictStageBytes`

Binary-safe sibling of `ReadConflictStage`. Instead of
`StandardOutput.ReadToEnd()` (which goes through UTF-8 decoding and
corrupts bytes), reads `StandardOutput.BaseStream` directly into a
`MemoryStream`. The `StreamReader` is never touched; bytes round-trip
1:1.

### `ImageMergeService` — `Services/Merge/ImageMergeService.cs`

Loads base/ours/theirs bytes from stages 1/2/3 and classifies each:

- **LFS pointer detection** via the well-known ASCII prefix
  `version https://git-lfs.github.com/spec/v1` — the prefix is
  distinctive enough that a collision with a real image is essentially
  zero
- **Magic-byte sniffing** for PNG, JPEG, GIF, BMP, WebP. SVG is
  text (non-binary) and never reaches this path.

Returns an `ImageConflictPayload` — a structured record the VM owns.

### `ImageConflictPane` — `Controls/Merge/ImageConflictPane.cs`

Custom `FrameworkElement` rendering five modes:

| Mode | Behaviour |
|---|---|
| `SideBySide` | Two halves, ours left, theirs right, divider |
| `OnionSkin` | Ours underneath, theirs on top at variable opacity |
| `Swipe` | Ours full area; theirs clipped to right of draggable split |
| `Difference` | Cached WriteableBitmap of amplified \|ours − theirs\| per pixel |
| `Overlay` | Both sides at 50% opacity over a transparency checkerboard |

Difference mode's pixel kernel runs on the thread pool via
`StartDifferenceBuild` — a 4K diff is ~67 MB and ~30 M iterations;
running inline would freeze the dispatcher. A "Computing difference…"
placeholder renders while the build is in flight. Result dropped if
`Payload` changes mid-compute (rapid file switch).

### LFS handling

When any side's `IsLfsPointer` is true, the pane shows a clear message:
"Git LFS pointer detected. Run 'git lfs pull' …". A smudge hook will
be added in a future phase (originally planned as audit §5.7); the
feature is not present in the initial Phase 6 ship.

### Viewport state

`ImageViewportState` — shared `INotifyPropertyChanged` object. Holds:

- `Zoom` (clamped 0.05–32)
- `Pan` (viewport pixels)
- `Mode` (one of the five above)
- `SwipeRatio` (clamped 0–1)
- `OnionSkinOpacity` (clamped 0–1)

Zoom is cursor-anchored: scrolling the wheel over a point keeps that
image-space point under the cursor through the zoom. "Reset View"
button restores zoom + pan to defaults without disturbing mode or
slider settings.

---

## 12. Extension points

If you're extending the merge engine, here's where to start:

| You want to… | Change this |
|---|---|
| Swap the AI provider | Point `AiMergeMcpServerPath` at a different MCP server. No code change. |
| Add a new conflict-resolution command (e.g. "accept base") | `MergeEditorViewModel` — new `[RelayCommand]`; hook to a new `ResolutionState` case in `MergeDocument.ComposeResolvedText`. |
| Add a new image rendering mode | `ImageMergeMode` enum + a new `DrawX` method in `ImageConflictPane.OnRender` switch. |
| Add a new binary-format preview | New `IPaneRenderer` (audit §6.4 planned abstraction). Meanwhile: stash the classification in `ImageSidePayload` and add a new `DrawFoo` in `ImageConflictPane`. |
| Extend the AI payload (e.g. add file-type-specific hints) | Bump the wire protocol: add a new optional field to `WireRequest`. Existing MCP servers ignore unknown fields. Document in `tools/leaf-merge-mcp/README.md`. |
| Reintroduce Result-pane manual edits | Remove `IsReadOnly = true` on `ResultPane.cs`. Implement per-range text mapping so only the touched range flips to `ResolutionState.Manual`. The `NotImplementedException` in `MergeEditorView.xaml.cs.OnResultTextChanged` is your reminder. |

---

## 13. Gotchas

- **Never log request/response content for AI calls.** Only timing + outcome.
  Grep `Log.Info("AiMerge"` before changing anything in `McpMergeAssistant`.
- **The commit gate is a string scan** not a marker-count. A source file
  that legitimately contains `<<<<<<<` in a comment block is fine.
  Only the full `<<<<<<<` → `=======` → `>>>>>>>` triad triggers the gate.
- **CRLF and trailing newlines** are preserved through the entire
  pipeline. `ComposeResolvedText` strips the final `\n` when
  `!HasTrailingNewline`; `NormaliseToLf` is applied to manual text
  before substitution.
- **Rapid file-switching** during word-diff or difference-image compute
  is handled by cancellation tokens and captured-at-start payload
  comparison. Don't add new async paths without this pattern.
- **`LfsPointer` with `Format = Unknown`** — consumers should check
  `IsLfsPointer` first. A future refactor may promote this to a distinct
  `ImageFormat.LfsPointer` value.
- **The default MCP server path is empty.** The consent dialog's "Send
  to MCP" button is inert until a real path is configured in settings.
- **Keyboard navigation prefers unresolved ranges.** `F8` skips
  resolved conflicts; if all are resolved it falls back to a simple
  wrap-around so the user can still re-review.

---

## 14. Test coverage

| Layer | File | Count |
|---|---|---|
| Merge engine | `tests/Leaf.Tests/Services/Merge/GitMergeFileEngineTests.cs` | fixture-based |
| Zdiff3 parser | `tests/Leaf.Tests/Services/Merge/ConflictMarkerParserTests.cs` | edge cases + CRLF + unicode |
| Composition | `tests/Leaf.Tests/Models/Merge/MergeDocumentTests.cs` | round-trip, ordering |
| Commit gate | `tests/Leaf.Tests/ViewModels/Merge/MergeEditorViewModelGateTests.cs` | 8 tests |
| Navigation | `…/MergeEditorViewModelNavigationTests.cs` | 10 tests |
| AI (VM) | `…/MergeEditorViewModelAiTests.cs` | 10 tests incl. context-slice edges |
| AI (transport) | `…/Services/Merge/McpMergeAssistantTests.cs` | 10 tests incl. Win32 wrap + broken-pipe |
| Word diff | `…/Services/Merge/WordDiffServiceTests.cs` | token splits + unicode |
| Image classify | `…/Services/Merge/ImageMergeServiceTests.cs` | all formats + LFS prefix |
| Viewport | `…/Controls/Merge/ImageViewportStateTests.cs` | clamp + reset + no-op |
| Image pixel math | `…/Controls/Merge/ImageConflictPaneTests.cs` | size-match kernel |

Total: 420+ tests. Run with
`dotnet test tests/Leaf.Tests/Leaf.Tests.csproj --filter FullyQualifiedName~Merge`.

---

## 15. What didn't land, and why

- **AST / semantic merge** — ruled out in the plan. Schesch et al. ASE 2024
  shows it underperforms text-based merge with a good diff algorithm.
- **Interactive rebase integration** — deferred to §5.5 after this ships.
- **`IPaneRenderer` abstraction** — planned in Phase 6 but the ImageConflictPane
  is currently standalone. The abstraction will be introduced when a second
  non-text renderer is needed (e.g. SVG diff, JSON structural diff).
- **LFS smudge hook** — depends on audit §5.7. When that lands, `ImageMergeService`
  will auto-detect pointer files and resolve them before classification.
- **Fit-to-screen button** — the "Reset View" button resets zoom to 1.0
  (natural pixel size). A true "fit" that scales the image to fill the
  pane would be a small addition to `ImageViewportState`.
- **Result-pane manual edits** — explicitly deferred in Phase 2c. The
  `NotImplementedException` thrown by `OnResultTextChanged` is intentional —
  the foot-gun comment documents exactly what needs to happen before the
  handler can be safely re-enabled.

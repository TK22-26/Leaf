# Vendored AvalonEdit

This directory contains the source of [ICSharpCode.AvalonEdit](https://github.com/icsharpcode/AvalonEdit),
vendored into Leaf under the MIT license (see `LICENSE-AvalonEdit.txt` at the repo
root for the full notice).

## Import provenance

- **Upstream:** https://github.com/icsharpcode/AvalonEdit
- **Imported tag:** `v6.3.1` (commit `862415d51eddc9eac93f462dbc522ffbf929cd52`)
- **Imported on:** 2026-04-17
- **Imported by:** Phase 2a of the three-way merge engine overhaul (plan §8)

## Why vendored

Owning the source lets us modify internals (expose virtualization primitives,
share the glyph/layout engine with custom read-only panes) without fighting the
NuGet package's public API surface. See `review-the-next-step-valiant-pie.md`
decision D3 for the full rationale.

## Leaf-only modifications

Preserve these across any future re-import from upstream:

1. **Namespace rename.** All `ICSharpCode.AvalonEdit` and
   `ICSharpCode.AvalonEdit.*` namespaces were bulk-renamed to
   `Leaf.TextEdit` / `Leaf.TextEdit.*`. Applies to `.cs`, `.xaml`, and `.xshd`
   files. The XSHD schema URI (`http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008`)
   was intentionally NOT renamed — it's a schema identifier, not a code namespace.

2. **`#nullable disable` header** at line 1 of every vendored `.cs` file.
   Leaf compiles with `Nullable=enable` + `TreatWarningsAsErrors`; the upstream
   source isn't fully nullable-annotated.

3. **`AssemblyAttributes.cs`** (new file, not from upstream). Declares
   `XmlnsPrefix` / `XmlnsDefinition` attributes so the `http://icsharpcode.net/sharpdevelop/avalonedit`
   XAML namespace URI resolves to `Leaf.TextEdit` for external XAML consumers.
   Upstream's `Properties/AssemblyInfo.cs` was removed (its contents conflict
   with Leaf's main `AssemblyInfo.cs`).

4. **Project file layout.** Build actions for vendored resources
   (`Search/*.png`, `themes/RightArrow.cur`, `Highlighting/Resources/*`) live
   in `src/Leaf/Leaf.csproj`, not in a separate AvalonEdit csproj. `UseWindowsForms=true`
   is set at the Leaf project level (AvalonEdit uses `System.Windows.Forms.Screen`
   and `System.Windows.Forms.Cursor` for multi-monitor positioning).

5. **Phase 2b modifications (applied):** Stripped unused modules
   (`Search/`, `Folding/`, `CodeCompletion/`, `Snippets/`). Verified that
   the vendored `TextView` already exposes the virtualization primitives
   Phase 2c needs (`GetVisualTopByDocumentLine`, `DefaultLineHeight`,
   `DocumentHeight`, `GetVisualLineFromVisualTop`, `WideSpaceWidth`,
   `DefaultBaseline`) — no additional exposure required. Introduced
   `Leaf.TextEdit.MergePaneGlyphLayout` (NOT from upstream) as the shared
   font-metrics service that all merge panes consume; it delegates line
   measurement to the same `TextFormatter.FormatLine` pipeline that
   `TextView.CalculateDefaultTextMetrics` uses, guaranteeing pixel parity.
   Rewrote `themes/generic.xaml` to reference only surviving resources
   (`TextEditor.xaml`, `themes/RightArrow.cur`) and the correct pack URI
   prefix (`/Leaf;component/…`, not `/Leaf.TextEdit;component/…`).

## Re-import procedure

When cherry-picking upstream bug fixes or security patches:

1. **Identify the commits** you want to cherry-pick (e.g. `git log v6.3.1..master --oneline`
   in a fresh clone of `icsharpcode/AvalonEdit`).
2. **Apply patches** to the files under `src/Leaf/TextEdit/`. Handle conflicts
   arising from the namespace rename (`ICSharpCode.AvalonEdit` → `Leaf.TextEdit`)
   manually.
3. **Preserve the `#nullable disable` header** on any new / touched files.
4. **Do NOT re-import `Properties/AssemblyInfo.cs`.** Leaf's main assembly
   info takes precedence.
5. **Run `dotnet build Leaf.sln`** — zero warnings expected.
6. **Run `dotnet test`** — 348+ passing.
7. **Update this file's "Imported commit" to reflect the new state** if the
   re-import is substantial.

For bulk re-imports (e.g. jumping to a new major version):

1. Clone the target tag into a scratch directory.
2. `rsync -a --delete <clone>/ICSharpCode.AvalonEdit/ src/Leaf/TextEdit/ \
   --exclude Properties/ --exclude ICSharpCode.AvalonEdit.csproj \
   --exclude ICSharpCode.AvalonEdit.snk --exclude AvalonEditNuGetPackageIcon.png`.
3. Re-apply the namespace-rename sed: `find src/Leaf/TextEdit -type f
   \( -name "*.cs" -o -name "*.xaml" -o -name "*.xshd" -o -name "*.xml" \)
   -print0 | xargs -0 sed -i 's|ICSharpCode\.AvalonEdit|Leaf.TextEdit|g'`.
4. Re-apply the nullable-disable header: `find src/Leaf/TextEdit -name "*.cs"
   | xargs -I{} sed -i '1i #nullable disable' {}`.
5. Re-create `src/Leaf/TextEdit/AssemblyAttributes.cs` if missing.
6. Re-apply any Phase 2b / Phase 2c modifications that were stripped by the rsync.
   Track those changes in this file so they can be cherry-picked back.

## Stripped-from-import

The following files from upstream were excluded during import:

- `ICSharpCode.AvalonEdit.csproj` — Leaf uses its own project file.
- `ICSharpCode.AvalonEdit.snk` — strong-naming is upstream's packaging concern.
- `AvalonEditNuGetPackageIcon.png` — NuGet-package-only asset.
- `Properties/` — `AssemblyInfo.cs` conflicts with Leaf's; `AssemblyAttributes.cs`
  in this directory carries the XmlnsDefinition attributes we actually need.
- `docs/` — standalone API documentation, not needed at runtime.

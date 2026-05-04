# Leaf

A modern WPF Git client for Windows with an interactive commit graph, AI-powered commit messages, integrated pull-request workflows, and full GitFlow / worktree / interactive-rebase / bisect support.

[![Build and Release](https://github.com/TK22-26/Leaf/actions/workflows/build.yml/badge.svg)](https://github.com/TK22-26/Leaf/actions/workflows/build.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![Windows](https://img.shields.io/badge/Platform-Windows-0078D6)
![License](https://img.shields.io/badge/License-Freeware-blue)

> 📚 **Full documentation lives in the [Leaf Wiki](https://github.com/TK22-26/Leaf/wiki).** This README is a tour; the wiki has the deep guides.

## Features

### Git graph & commits
- Interactive commit graph with lane allocation, color-coded branches, and pass-through rendering of long-running lanes
- Live working-changes "WIP" row anchored to the current branch
- Click any commit to see metadata, signature status, changed-files tree, and per-file diffs
- Search commits inline; jump to a SHA from any tooltip or signature popup
- **Tag browser** (§5.17) — full tag detail pane with annotated message and target commit

### Repository management
- Clone from GitHub or Azure DevOps via in-app browsers, or any Git URL
- Pinned / Recent / custom Repository Groups, plus folder-watching for auto-discovery
- Per-repository GitFlow config, branch filters (hide / solo), color overrides, and palette persistence (§5.14)
- **Worktrees** — create, switch, lock, prune; first-class sidebar entries

### Working changes
- Stage / unstage individual files, hunks, or all
- Live diff viewer with hunk-level revert
- Conventional Commits structured form + freeform mode (§5.15) with per-repo and global templates
- AI commit-message generation (Claude / Gemini / Codex), schema-validated output
- **Commit signing** (§5.8) — GPG and SSH commit signatures; verification badges on every node

### Branching, merging, history rewriting
- Create / checkout / delete / rename branches with safety prompts
- Merge with conflict detection; **unrelated histories** prompt; squash / fast-forward / no-ff selection
- Full **merge editor** with three-way diff, side-by-side / inline views, AI-assisted resolution, per-conflict notes
- **Interactive rebase** window — pick / reword / squash / edit / drop with drag-reorder
- **Bisect** workflow with banner, automatic next-commit checkout, and full-content takeover view
- **Reflog** window with filters and context actions (§5.4)
- **Cherry-pick / revert / amend** from any commit

### Patch workflow (§5.10)
- Create patches from any commit range or working tree
- Apply patches via `git am` or `git apply` with conflict resolution piped through the merge editor

### Stashes & submodules
- Stash with optional message, view inline above HEAD, pop / drop / apply
- Submodule add / update / sync with progress UI

### Pull requests (M1)
- Browse, open, and create PRs against **GitHub** and **Azure DevOps** without leaving Leaf
- Inline file diffs, status checks, reviewers, and merge actions

### AI integration
- **Claude** — uses the rolling `sonnet` alias (auto-tracks Anthropic releases)
- **Gemini** — uses the gemini CLI default (auto-tracks Google releases)
- **Codex** — uses your `~/.codex/config.toml` model (auto-tracks OpenAI's ChatGPT-account-eligible model rotation)
- AI also assists during merge-conflict resolution (Merge Editor's Ask AI)

### Authentication
- **GitHub PAT** with `repo` scope
- **Azure DevOps PAT** with `Code (Read/Write)` and `PullRequest` scopes
- **SSH key management** (§5.13) — generate, list, add to ssh-agent, push to GitHub, all from inside Leaf
- **Git Credential Manager** fallback for everything else

### Customisation
- Configurable **keyboard shortcuts** (§5.9) — every action remappable, conflict detection
- **External tools** — pluggable diff/merge tools (BeyondCompare, KDiff3, etc.) and editors
- **Branch colors** (§5.14) — palette presets + per-branch overrides, persisted to `.git/config`
- **Commit templates** (§5.15) — Conventional Commits form, custom templates, master toggle, per-repo defaults
- Light / dark Fluent themes

### CLI flags
- `--repo <path>` / `-r <path>` — open Leaf and navigate directly to a repo
- `--auto-commit <repoName>` / `-ac` — headless AI auto-commit (great for cron / CI)
- `--help` / `-h`

## Installation

### Prerequisites
- Windows 10/11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git for Windows](https://git-scm.com/download/win)

### Get Leaf
- **Released installer:** see the [Releases page](https://github.com/TK22-26/Leaf/releases) for signed builds.
- **Build from source:**
  ```bash
  git clone https://github.com/TK22-26/Leaf.git
  cd Leaf
  dotnet build Leaf.sln
  dotnet run --project src/Leaf/Leaf.csproj
  ```

## Quick configuration

| Area | Where | Wiki link |
|------|-------|-----------|
| GitHub auth (PAT) | Settings → Authentication → GitHub | [Authentication](https://github.com/TK22-26/Leaf/wiki/Authentication-and-SSH) |
| Azure DevOps auth | Settings → Authentication → Azure DevOps | [Authentication](https://github.com/TK22-26/Leaf/wiki/Authentication-and-SSH) |
| AI providers | Settings → AI Integrations | [AI Integration](https://github.com/TK22-26/Leaf/wiki/AI-Integration) |
| GitFlow defaults | Settings → GitFlow | [Branches & GitFlow](https://github.com/TK22-26/Leaf/wiki/Branches-and-GitFlow) |
| Commit signing | Settings → Commit Signing | [Commit Signing](https://github.com/TK22-26/Leaf/wiki/Commit-Signing) |
| SSH keys | Settings → SSH Keys | [SSH Keys](https://github.com/TK22-26/Leaf/wiki/Authentication-and-SSH#ssh-keys) |
| Templates | Settings → Commit Templates | [Commit Templates](https://github.com/TK22-26/Leaf/wiki/Commit-Templates) |
| Shortcuts | Settings → Keyboard Shortcuts | [Shortcuts & CLI](https://github.com/TK22-26/Leaf/wiki/Keyboard-Shortcuts-and-CLI) |
| External tools | Settings → External Tools | [Settings Reference](https://github.com/TK22-26/Leaf/wiki/Settings-Reference) |

## Default keyboard shortcuts

A small set of defaults; everything is remappable in Settings → Keyboard Shortcuts.

| Action | Shortcut |
|--------|----------|
| Refresh | `F5` |
| Commit | `Ctrl+Enter` |
| Stage all | `Ctrl+Shift+S` |
| New branch | `Ctrl+B` |
| Command palette | `Ctrl+K` |
| AI commit message | `Ctrl+Shift+A` |
| Open terminal | `` Ctrl+` `` |
| Pull / Push | `Ctrl+Shift+P` / `Ctrl+P` |

## Architecture

```
src/
├── Leaf/                  WPF app — UI, view models, services
│   ├── Controls/          Custom controls (GitGraphCanvas, CommitInput, etc.)
│   ├── Graph/             Lane allocator + branch-color resolver
│   ├── Models/            Domain types (CommitInfo, BranchLabel, …)
│   ├── Services/          GitService, CredentialService, AiCommitMessageService, …
│   │   └── Git/Operations/  Per-domain git operations (Branch, Merge, Rebase, …)
│   ├── ViewModels/        MainViewModel split by partial files (Branch, Merge, Sync, …)
│   └── Views/             XAML windows, dialogs, settings controls
├── Leaf.Core/             Shared models / engine logic usable from headless contexts
├── Leaf.AskPass/          Standalone helper invoked by git for credential prompts
└── Leaf.SequenceEditor/   Standalone helper invoked by git for interactive rebase todos
```

See [Architecture](https://github.com/TK22-26/Leaf/wiki/Architecture) on the wiki for a deeper tour.

## Dependencies

- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM source generators
- [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) — fast read paths and simple mutations
- Git CLI — used for any operation with an editor / hook / credential contract
- [DiffPlex](https://github.com/mmanela/diffplex) — diff and three-way merge engine
- [WPF-UI (Fluent)](https://github.com/lepoco/wpfui) — Fluent design controls and theming

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (Leaf will happily generate the message for you 😉)
4. Push and open a Pull Request

See [Contributing](https://github.com/TK22-26/Leaf/wiki/Contributing) for the development workflow, build / test instructions, and the architectural conventions Leaf follows.

## License

This software is **Freeware** — free to use, modify, and distribute for non-commercial purposes.

**You may NOT sell this software or any derivative works for profit.**

See the [LICENSE](LICENSE) file for full terms.

## Acknowledgments

- Git graph visualization inspired by GitKraken and Sublime Merge
- UI design follows Windows 11 Fluent Design principles
- AI integration powered by Anthropic Claude, Google Gemini, and OpenAI Codex

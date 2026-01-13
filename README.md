# Leaf

A modern WPF Git client for Windows with an interactive commit graph, AI-powered commit messages, and seamless cloud repository integration.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![Windows](https://img.shields.io/badge/Platform-Windows-0078D6)
![License](https://img.shields.io/badge/License-Freeware-blue)

## Features

### Git Graph Visualization
- Interactive commit history with branch lanes and merge lines
- Color-coded branches for easy identification
- Click to view commit details, changed files, and diffs
- Visual indicators for HEAD, remote tracking, and tags

### Repository Management
- **Clone repositories** from GitHub, Azure DevOps, or any Git URL
- **Browse remote repositories** directly from GitHub and Azure DevOps
- **Recent repositories** list for quick access
- **Repository groups** to organize your projects

### Branch Operations
- Create, checkout, and delete branches
- Merge branches with conflict detection
- **Unrelated histories support** - automatically prompts when merging branches with no common ancestor
- Visual branch list organized by local, remote, and tags

### Working Changes
- Stage/unstage individual files or all changes
- View file diffs before committing
- Discard changes with confirmation
- Character count for commit messages (72 char recommended limit)

### AI-Powered Commit Messages
Generate meaningful commit messages automatically using your preferred AI:
- **Claude** (Anthropic) - via Claude CLI
- **Gemini** (Google) - via Gemini CLI
- **Codex** (OpenAI) - via Codex CLI

The AI analyzes your staged changes and generates:
- A concise commit message (≤72 characters)
- A bullet-point description of changes

### Merge Conflict Resolution
- Visual conflict list showing all conflicting files
- Three-way merge view (Base, Ours, Theirs)
- Per-file resolution: Accept Ours, Accept Theirs, or Manual Edit
- Merge status banner with quick actions

### Stash Support
- Stash working changes with optional message
- View stash list with timestamps
- Pop or delete individual stashes
- Smart stash handling during branch operations

### Authentication
- **GitHub** - Personal Access Token (PAT) authentication
- **Azure DevOps** - PAT with organization configuration
- **Git Credential Manager** - Automatic fallback for other remotes

## Installation

### Prerequisites
- Windows 10/11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git for Windows](https://git-scm.com/download/win)

### Build from Source
```bash
git clone https://github.com/TK22-26/Leaf.git
cd Leaf
dotnet restore
dotnet build
dotnet run --project src/Leaf/Leaf.csproj
```

## Configuration

### GitHub Authentication
1. Go to **Settings** → **Authentication** → **GitHub**
2. Enter your GitHub username
3. Create a [Personal Access Token](https://github.com/settings/tokens) with `repo` scope
4. Paste the token and click **Connect**

### Azure DevOps Authentication
1. Go to **Settings** → **Authentication** → **Azure DevOps**
2. Enter your organization name (e.g., `mycompany` from `dev.azure.com/mycompany`)
3. Create a [PAT](https://docs.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate) with `Code (Read)` scope
4. Paste the token and click **Connect**

### AI Integration
Leaf integrates with CLI tools for AI-powered features. Install your preferred AI CLI:

#### Claude CLI
```bash
npm install -g @anthropic-ai/claude-code
claude auth login
```

#### Gemini CLI
```bash
npm install -g @anthropic-ai/gemini-cli
gemini auth login
```

#### Codex CLI
```bash
npm install -g @openai/codex
codex auth login
```

Then in Leaf:
1. Go to **Settings** → **AI Integrations**
2. Click **Connect** next to your installed CLI
3. Select your **Default AI** from the dropdown
4. Adjust **CLI timeout** if needed (default: 60s)

## Usage

### Cloning a Repository
1. Click **Clone** in the toolbar
2. Choose a tab:
   - **Browse GitHub** - Select from your GitHub repositories
   - **Browse Azure DevOps** - Select from your organization's repositories
   - **Enter URL** - Paste any Git repository URL
3. Choose destination folder
4. Click **Clone**

### Making a Commit
1. Make changes to your files
2. In the **Working Changes** panel:
   - Click **+** to stage individual files, or **Stage All**
   - Enter a commit message (≤72 characters recommended)
   - Optionally add a description
   - Click **Commit**

### Using AI Commit Messages
1. Stage your changes
2. Click the **✨** (sparkle) button next to the commit message
3. Wait for the AI to analyze your changes
4. Review and edit the generated message if needed
5. Click **Commit**

### Merging Branches
1. Right-click a branch in the branch list
2. Select **Merge into current branch**
3. If branches have unrelated histories, a dialog will prompt you to confirm
4. Resolve any conflicts if they occur

### Resolving Merge Conflicts
1. When conflicts occur, the **Merge Status** banner appears
2. Click on a conflicting file to open the resolution view
3. For each file, choose:
   - **Accept Ours** - Keep your changes
   - **Accept Theirs** - Take incoming changes
   - **Manual Edit** - Edit the merged result manually
4. Click **Mark Resolved** after resolving each file
5. Click **Complete Merge** when all conflicts are resolved

## Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| Refresh | `F5` |
| Commit | `Ctrl+Enter` |
| Stage All | `Ctrl+Shift+S` |
| New Branch | `Ctrl+B` |

## Architecture

```
src/Leaf/
├── Controls/          # Custom WPF controls (GitGraphCanvas, etc.)
├── Converters/        # XAML value converters
├── Graph/             # Git graph layout algorithms
├── Models/            # Data models (CommitInfo, BranchInfo, etc.)
├── Resources/         # XAML styles and themes
├── Services/          # Business logic (GitService, CredentialService, etc.)
├── ViewModels/        # MVVM ViewModels
└── Views/             # XAML views and dialogs
```

### Key Components
- **GitService** - All Git operations via command-line git
- **GitGraphCanvas** - Custom WPF canvas for commit graph rendering
- **ThreeWayMergeService** - Conflict detection and resolution using DiffPlex
- **CredentialService** - Secure credential storage via Windows Credential Manager

## Dependencies

- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM framework
- [DiffPlex](https://github.com/mmanela/diffplex) - Diff algorithm for merge resolution
- [WPF-UI (Fluent)](https://github.com/lepoco/wpfui) - Modern UI styling

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This software is **Freeware** - free to use, modify, and distribute for non-commercial purposes.

**You may NOT sell this software or any derivative works for profit.**

See the [LICENSE](LICENSE) file for full terms.

## Acknowledgments

- Git graph visualization inspired by GitKraken and Sublime Merge
- UI design follows Windows 11 Fluent Design principles
- AI integration powered by Anthropic Claude, Google Gemini, and OpenAI Codex

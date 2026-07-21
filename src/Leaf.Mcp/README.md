# Leaf.Mcp — whole-tree git operations for AI agents

A stdio [MCP](https://modelcontextprotocol.io) server that exposes Leaf's
repository-tree engine to AI coding agents. One `leaf_status` call covers a
repository **and every (nested) submodule**, and `leaf_commit` / `leaf_push`
apply the dependency ordering a submodule tree requires — so agents stop
missing dirty submodules and unpushed pointer bumps (issue #39).

## Registration

```
claude mcp add leaf -- "<install-dir>\Leaf.Mcp.exe"
```

For a debug build:

```
claude mcp add leaf -- "<repo>\src\Leaf.Mcp\bin\Debug\net10.0-windows\Leaf.Mcp.exe"
```

Test interactively with `npx @modelcontextprotocol/inspector <path>\Leaf.Mcp.exe`.

## Tools

| Tool | What it does |
|------|--------------|
| `leaf_status` | Status of the whole tree in one call: per repo branch, ahead/behind, staged/unstaged files, merge-in-progress, submodule pointer drift, plus dirty/unpushed counts. |
| `leaf_commit` | Commits submodules first, stages the updated gitlink pointers in each parent, then commits the parent. Per-repo messages via `messages` (root-relative paths, `"."` = root); dirty repos without a message fail loudly and their ancestors are skipped. |
| `leaf_push` | Pushes submodules first; ancestors are skipped when a descendant push fails so no dangling gitlinks are ever published. |
| `leaf_pull` / `leaf_fetch` | Parallel across the tree; a conflicted pull fails only that repo. |
| `leaf_repos` | Lists repositories registered in the Leaf GUI (discovery only — registration is not required for the other tools). |

All tools take an optional `path` (defaults to the working directory) and
always resolve the **outermost** enclosing repository — calling from inside a
submodule still operates on the whole tree.

## Behavior notes

- All writes go through the git CLI, so running alongside the Leaf GUI is
  coordinated by git's own `index.lock`; collisions fail loudly and are safe
  to retry. The GUI's file watcher picks up MCP-made commits automatically.
- stdout is reserved for JSON-RPC. Logging goes to stderr and to
  `%LOCALAPPDATA%\Leaf\leaf.log` (set `LEAF_MCP_VERBOSE=1` for per-command
  git logging).
- Ordering, pointer staging, and skip semantics live in
  `Leaf.Services.RepoTree.RepoTreeService`, shared with the workspace grid —
  the MCP server contains no git logic of its own.

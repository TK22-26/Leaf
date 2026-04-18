# leaf-merge-mcp — Reference MCP Server for Leaf's AI Merge Assistant

Leaf's AI-assisted conflict resolution (plan §5.5, Phase 5) talks to an
external process over **stdio JSON**. This directory is the *reference*
server implementation — the default path Leaf's Settings points at. Users
are free to swap in their own server (local model, corporate endpoint,
Gemini, etc.); the contract below is the only thing that matters.

## Wire contract

Leaf writes **exactly one** JSON object on the server's stdin, closes
stdin, then reads **exactly one** JSON object from the server's stdout
and waits for the process to exit.

### Request shape

```json
{
  "tool": "resolve_conflict",
  "filePath": "src/Example.cs",
  "language": "csharp",
  "baseLines":    ["line 1", "line 2"],
  "oursLines":    ["line 1", "different line 2"],
  "theirsLines":  ["line 1", "another line 2"],
  "contextBefore": ["line A before", "line B before"],
  "contextAfter":  ["line A after", "line B after"]
}
```

No branch names, commit messages, or other repo state are ever included
— this is enforced by the Leaf client, not by the server.

### Response shape

```json
{
  "proposedText": "line 1\nresolved line 2",
  "rationale": "Ours renamed the variable; theirs changed its value; kept both.",
  "confidence": "high"
}
```

- `proposedText` — the resolved region text, LF line endings, no
  trailing newline unless the original had one
- `rationale` — one-sentence human explanation (shown in the preview dialog)
- `confidence` — `"high"` / `"medium"` / `"low"`

### Error conventions

- Non-zero exit code: Leaf surfaces stderr as an `AiMergeAssistantException`
- Malformed JSON on stdout: Leaf surfaces a parse-error message
- Timeout: Leaf cancels by killing the process tree

## Default server implementation

The reference server is intentionally minimal and is meant to be replaced
with a real model integration. Fork this directory, wire it to your
model/provider of choice, and point Settings → AI Merge → MCP Server Path
at the resulting executable.

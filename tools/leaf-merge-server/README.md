# leaf-merge-server — Reference External-Server Backend for Leaf's AI Merge Assistant

Leaf's AI-assisted conflict resolution can talk to an external process
over **stdio JSON**. This directory documents the wire contract that
Settings → AI → Merge Assistant → External Server expects when the user
picks "External Server" as the provider.

Most users won't need this. Leaf also ships per-provider CLI backends
(Claude / Gemini / Codex / Ollama) which reuse the user's existing
provider tooling and require no separate server. The External-Server
option exists for power users / corporate setups that need a custom
backend (on-prem model, audit-logging proxy, alternative protocol
shim, etc.).

The contract below is the only thing that matters; reference
implementations may be added later, or you can wire your own.

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

## Wiring it up

Build (or download) an executable that implements the contract above,
then in Leaf go to **Settings → AI → Merge Assistant**, set the provider
dropdown to **External Server**, and point **External Server Path** at
your executable.

Leaf shells the executable out fresh for each request — there is no
session, no warm-up, no shared state. A crashed server doesn't leave
Leaf with stale state, at the cost of a small per-request spawn.

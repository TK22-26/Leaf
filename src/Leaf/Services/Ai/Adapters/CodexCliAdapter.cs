#nullable enable
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Adapters;

/// <summary>
/// Adapter for the <c>codex exec</c> CLI. Codex needs the JSON schema
/// passed as a file path (<c>--output-schema</c>), and emits a JSONL
/// stream — its <c>agent_message</c> event carries the structured
/// payload we want.
/// </summary>
/// <remarks>
/// <para>
/// The schema-file cache keys on the SHA-256 of the schema text so the
/// same schema across calls reuses the same temp file (no FS churn under
/// load). Files leak for the process lifetime; the OS temp folder
/// reclaims them — this matches the original
/// <c>AiCommitMessageService.GetOrCreateCodexSchemaFile</c> behaviour
/// just generalised across multiple schemas.
/// </para>
/// <para>
/// We deliberately do <em>not</em> pin a specific model with <c>-m</c>.
/// OpenAI rotates the ChatGPT-account-eligible model list (e.g.
/// gpt-5.1-codex-mini was hard-locked out in late 2026 with "model is
/// not supported when using Codex with a ChatGPT account"). Letting
/// <c>codex exec</c> fall back to whatever the user's
/// <c>~/.codex/config.toml</c> declares as <c>model = "..."</c> keeps
/// us working across future rotations without code changes.
/// </para>
/// </remarks>
public sealed class CodexCliAdapter : IAiCliAdapter
{
    // SHA-256(schema)[..16] → temp file path. ConcurrentDictionary so
    // concurrent invocations across different features don't double-write.
    private static readonly ConcurrentDictionary<string, string> SchemaFileCache = new(StringComparer.Ordinal);

    public AiProviderKind Provider => AiProviderKind.Codex;

    public AiCliInvocation BuildInvocation(string prompt, string jsonSchema, string? repoPath)
    {
        var schemaPath = GetOrCreateSchemaFile(jsonSchema);
        return new AiCliInvocation(
            Executable: "codex",
            Arguments: new[]
            {
                "exec",
                // Cheap/fast tier — appropriate for both commit-message
                // generation and per-conflict resolution; neither needs
                // chain-of-thought.
                "-c", "model_reasoning_effort=low",
                "--full-auto",
                "--color", "never",
                "--output-schema", schemaPath,
                "--json",
                "-",
            },
            Stdin: prompt,
            WorkingDirectory: repoPath);
    }

    public string ExtractStructuredOutput(string rawStdout)
        => CommitMessageParser.ExtractCodexJsonlMessage(rawStdout);

    private static string GetOrCreateSchemaFile(string jsonSchema)
    {
        // Hash the schema text so equivalent schemas share a file; the
        // first 16 hex chars (64 bits) is plenty for collision resistance
        // with the handful of schemas in play.
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(jsonSchema)))[..16].ToLowerInvariant();

        if (SchemaFileCache.TryGetValue(hash, out var cached) && File.Exists(cached))
        {
            return cached;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "Leaf");
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, $"codex-schema-{hash}.json");

        // Write atomically: another caller may race us — File.WriteAllText
        // is fine because the contents are byte-identical (same hash).
        File.WriteAllText(path, jsonSchema);

        SchemaFileCache[hash] = path;
        return path;
    }
}

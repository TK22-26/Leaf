#nullable enable
using FluentAssertions;
using Leaf.Services.Ai.Adapters;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Ai.Adapters;

/// <summary>
/// Tests that each <see cref="IAiCliAdapter"/> builds the CLI invocation
/// shape we expect: correct executable name, the right argument list,
/// stdin routing, and (where applicable) the JSON schema reaching the
/// CLI. These pin the contract so a future "let's add a flag" change
/// fails the test instead of silently shipping.
/// </summary>
public class AdapterInvocationTests
{
    private const string SamplePrompt = "PROMPT BODY";
    private const string SampleSchema = """{"type":"object","properties":{"x":{"type":"string"}}}""";

    [Fact]
    public void Claude_BuildInvocation_HasExpectedArgs()
    {
        var inv = new ClaudeCliAdapter().BuildInvocation(SamplePrompt, SampleSchema, repoPath: "C:/repo");

        inv.Executable.Should().Be("claude");
        inv.Stdin.Should().Be(SamplePrompt);
        inv.WorkingDirectory.Should().Be("C:/repo");
        inv.Arguments.Should().ContainInOrder("-p", "--model", "sonnet", "--output-format", "json");
        inv.Arguments.Should().ContainInOrder("--json-schema", SampleSchema);
        // Trailing "-" tells claude to read prompt body from stdin.
        inv.Arguments.Last().Should().Be("-");
    }

    [Fact]
    public void Claude_Provider_IsClaude()
        => new ClaudeCliAdapter().Provider.Should().Be(AiProviderKind.Claude);

    [Fact]
    public void Gemini_BuildInvocation_HasExpectedArgs()
    {
        var inv = new GeminiCliAdapter().BuildInvocation(SamplePrompt, SampleSchema, repoPath: null);

        inv.Executable.Should().Be("gemini");
        inv.Stdin.Should().Be(SamplePrompt);
        inv.WorkingDirectory.Should().BeNull();
        inv.Arguments.Should().ContainInOrder("-p", "-", "--output-format", "json");
        // Gemini doesn't accept a schema flag; the schema arg should NOT
        // leak into the invocation.
        inv.Arguments.Should().NotContain(SampleSchema);
        inv.Arguments.Should().NotContain("--json-schema");
    }

    [Fact]
    public void Gemini_Provider_IsGemini()
        => new GeminiCliAdapter().Provider.Should().Be(AiProviderKind.Gemini);

    [Fact]
    public void Codex_BuildInvocation_HasExpectedArgs_AndWritesSchemaFile()
    {
        var inv = new CodexCliAdapter().BuildInvocation(SamplePrompt, SampleSchema, repoPath: "C:/repo");

        inv.Executable.Should().Be("codex");
        inv.Stdin.Should().Be(SamplePrompt);
        inv.WorkingDirectory.Should().Be("C:/repo");
        inv.Arguments.Should().ContainInOrder("exec", "-c", "model_reasoning_effort=low");
        inv.Arguments.Should().Contain("--full-auto");
        inv.Arguments.Should().ContainInOrder("--color", "never");
        inv.Arguments.Should().Contain("--json");
        inv.Arguments.Last().Should().Be("-");

        // Schema goes via --output-schema <file>; the file must exist
        // and contain the schema text we asked for.
        var schemaFlagIndex = inv.Arguments.ToList().IndexOf("--output-schema");
        schemaFlagIndex.Should().BeGreaterThan(-1);
        var schemaPath = inv.Arguments[schemaFlagIndex + 1];
        System.IO.File.Exists(schemaPath).Should().BeTrue();
        System.IO.File.ReadAllText(schemaPath).Should().Be(SampleSchema);
    }

    [Fact]
    public void Codex_Provider_IsCodex()
        => new CodexCliAdapter().Provider.Should().Be(AiProviderKind.Codex);

    [Fact]
    public void Codex_SameSchemaAcrossCalls_SharesFile()
    {
        var inv1 = new CodexCliAdapter().BuildInvocation(SamplePrompt, SampleSchema, repoPath: null);
        var inv2 = new CodexCliAdapter().BuildInvocation(SamplePrompt, SampleSchema, repoPath: null);

        var path1 = inv1.Arguments[inv1.Arguments.ToList().IndexOf("--output-schema") + 1];
        var path2 = inv2.Arguments[inv2.Arguments.ToList().IndexOf("--output-schema") + 1];
        path1.Should().Be(path2, "identical schemas should share a single temp file (no FS churn)");
    }

    [Fact]
    public void Codex_DifferentSchemasAcrossCalls_GetSeparateFiles()
    {
        var schemaA = """{"type":"object","properties":{"a":{"type":"string"}}}""";
        var schemaB = """{"type":"object","properties":{"b":{"type":"string"}}}""";

        var invA = new CodexCliAdapter().BuildInvocation(SamplePrompt, schemaA, repoPath: null);
        var invB = new CodexCliAdapter().BuildInvocation(SamplePrompt, schemaB, repoPath: null);

        var pathA = invA.Arguments[invA.Arguments.ToList().IndexOf("--output-schema") + 1];
        var pathB = invB.Arguments[invB.Arguments.ToList().IndexOf("--output-schema") + 1];
        pathA.Should().NotBe(pathB);
        System.IO.File.ReadAllText(pathA).Should().Be(schemaA);
        System.IO.File.ReadAllText(pathB).Should().Be(schemaB);
    }
}

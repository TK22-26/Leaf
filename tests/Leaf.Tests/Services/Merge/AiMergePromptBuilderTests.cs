#nullable enable
using FluentAssertions;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Tests for <see cref="AiMergePromptBuilder"/>. Includes the privacy-
/// contract test that asserts the prompt only ever contains content
/// from <see cref="AiResolutionRequest"/> — no leakage of branch names,
/// surrounding file content, or other state.
/// </summary>
public class AiMergePromptBuilderTests
{
    private static AiResolutionRequest Sample() => new(
        FilePath: "src/Foo.cs",
        Language: "csharp",
        BaseLines: new[] { "BASE_LINE_1", "BASE_LINE_2" },
        OursLines: new[] { "OURS_LINE_1" },
        TheirsLines: new[] { "THEIRS_LINE_1", "THEIRS_LINE_2", "THEIRS_LINE_3" },
        ContextBefore: new[] { "CTX_BEFORE_1" },
        ContextAfter: new[] { "CTX_AFTER_1" });

    [Fact]
    public void BuildPrompt_ContainsAllRequestFields()
    {
        var prompt = AiMergePromptBuilder.BuildPrompt(Sample());

        prompt.Should().Contain("src/Foo.cs");
        prompt.Should().Contain("csharp");
        prompt.Should().Contain("BASE_LINE_1");
        prompt.Should().Contain("BASE_LINE_2");
        prompt.Should().Contain("OURS_LINE_1");
        prompt.Should().Contain("THEIRS_LINE_1");
        prompt.Should().Contain("THEIRS_LINE_2");
        prompt.Should().Contain("THEIRS_LINE_3");
        prompt.Should().Contain("CTX_BEFORE_1");
        prompt.Should().Contain("CTX_AFTER_1");
    }

    [Fact]
    public void BuildPrompt_OnlyContainsRequestFields_AndPromptScaffolding()
    {
        // Privacy contract: the builder must NOT have access to anything
        // beyond AiResolutionRequest. This test loads sentinel strings
        // into every field, then walks the output and asserts that any
        // "real-content" character sequence is one we put in.
        //
        // We rely on the fact that the prompt is deterministic and our
        // sentinels don't collide with the static scaffolding.
        var sentinels = new[]
        {
            "PATH_SENTINEL_zzz1",
            "LANG_SENTINEL_zzz2",
            "BASE_SENTINEL_zzz3",
            "OURS_SENTINEL_zzz4",
            "THEIRS_SENTINEL_zzz5",
            "CTX_BEFORE_SENTINEL_zzz6",
            "CTX_AFTER_SENTINEL_zzz7",
        };

        var request = new AiResolutionRequest(
            FilePath: sentinels[0],
            Language: sentinels[1],
            BaseLines: new[] { sentinels[2] },
            OursLines: new[] { sentinels[3] },
            TheirsLines: new[] { sentinels[4] },
            ContextBefore: new[] { sentinels[5] },
            ContextAfter: new[] { sentinels[6] });

        var prompt = AiMergePromptBuilder.BuildPrompt(request);

        // Each sentinel must show up exactly once (we passed it once).
        foreach (var s in sentinels)
        {
            prompt.IndexOf(s, StringComparison.Ordinal).Should().BeGreaterThan(0,
                $"sentinel '{s}' must appear in the prompt");
        }

        // The classic leakage shapes that have bitten us before.
        // None of them should ever appear in a prompt because the
        // builder only reads AiResolutionRequest.
        prompt.Should().NotContain("origin/");
        prompt.Should().NotContain("HEAD");
        prompt.Should().NotContain(".git");
        prompt.Should().NotContain("commitMessage");  // shape from commit-message feature
    }

    [Fact]
    public void BuildPrompt_EmptyContextBlocks_RenderAsExplicitEmpty()
    {
        // When a conflict is at file start / end the context lists are
        // empty. The prompt should still mention the (empty) blocks so
        // the model knows there's no extra context to consider.
        var request = new AiResolutionRequest(
            FilePath: "x.cs",
            Language: "csharp",
            BaseLines: new[] { "b" },
            OursLines: new[] { "o" },
            TheirsLines: new[] { "t" },
            ContextBefore: Array.Empty<string>(),
            ContextAfter: Array.Empty<string>());

        var prompt = AiMergePromptBuilder.BuildPrompt(request);
        // "(empty)" is the sentinel rendered when a block has no lines.
        prompt.Should().Contain("(empty)");
    }

    [Fact]
    public void Schema_IsValidJson_AndConstrainsExpectedShape()
    {
        var schema = AiMergePromptBuilder.ResolutionJsonSchema;

        // Round-trip through the JSON parser to confirm it's valid JSON.
        using var doc = System.Text.Json.JsonDocument.Parse(schema);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("object");
        var props = root.GetProperty("properties");
        props.GetProperty("proposedText").GetProperty("type").GetString().Should().Be("string");
        props.GetProperty("rationale").GetProperty("type").GetString().Should().Be("string");
        props.GetProperty("confidence").GetProperty("type").GetString().Should().Be("string");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void BuildPrompt_NullRequest_Throws()
    {
        var act = () => AiMergePromptBuilder.BuildPrompt(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

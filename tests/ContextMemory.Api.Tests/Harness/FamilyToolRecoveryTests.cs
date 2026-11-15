using System.Text.Json;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Agentic.Harness;
using ContextMemory.Core.Agentic.Harness.Families;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class FamilyToolRecoveryTests
{
    private static readonly IReadOnlySet<string> AllowAll =
        new HashSet<string>(StringComparer.Ordinal);

    public static IEnumerable<object[]> FamilyMarkupCases()
    {
        const char bar = '\uFF5C';
        const char block = '\u2581';

        yield return
        [
            "qwen",
            """
            <tool_call>
            {"name":"search","arguments":{"query":"weather"}}
            </tool_call>
            """
        ];

        yield return
        [
            "deepseek-r1",
            $"<{bar}tool{block}calls{block}begin{bar}>" +
            $"<{bar}tool{block}call{block}begin{bar}>function<{bar}tool{block}sep{bar}>search\n" +
            "```json\n{\"query\":\"weather\"}\n```\n" +
            $"<{bar}tool{block}call{block}end{bar}>" +
            $"<{bar}tool{block}calls{block}end{bar}>"
        ];

        yield return
        [
            "dsml",
            $"<{bar}DSML{bar}tool_calls>" +
            $"<{bar}DSML{bar}invoke name=\"search\">" +
            $"<{bar}DSML{bar}parameter name=\"query\">weather</{bar}DSML{bar}parameter>" +
            $"</{bar}DSML{bar}invoke>" +
            $"</{bar}DSML{bar}tool_calls>"
        ];

        yield return
        [
            "gemma4",
            """<|tool_call>call:search{"query":"weather"}<tool_call|>"""
        ];

        yield return
        [
            "harmony",
            """to=functions.search {"query":"weather"}"""
        ];

        yield return
        [
            "mistral",
            """[TOOL_CALLS] [{"name":"search","arguments":{"query":"weather"}}]"""
        ];

        yield return
        [
            "llama",
            """<|python_tag|>{"name":"search","parameters":{"query":"weather"}}"""
        ];

        yield return
        [
            "kimi",
            "<|tool_calls_section_begin|>" +
            "<|tool_call_begin|>functions.search:0<|tool_call_argument_begin|>" +
            """{"query":"weather"}""" +
            "<|tool_call_end|>" +
            "<|tool_calls_section_end|>"
        ];

        yield return
        [
            "glm",
            """
            <tool_call>search
            <arg_key>query</arg_key><arg_value>weather</arg_value>
            </tool_call>
            """
        ];

        yield return
        [
            "granite",
            """
            <tool_call>
            {"name":"search","arguments":{"query":"weather"}}
            </tool_call>
            """
        ];

        yield return
        [
            "minimax",
            """
            <invoke name="search"><parameter name="query">weather</parameter></invoke>
            """
        ];

        yield return
        [
            "minimax-text",
            "```typescript\nfunctions.search({\"query\":\"weather\"})\n```"
        ];

        yield return
        [
            "olmo",
            """
            <function_calls>
            search(query="weather")
            </function_calls>
            """
        ];

        yield return
        [
            "phi4",
            """functools[{"name":"search","arguments":{"query":"weather"}}]"""
        ];
    }

    [Theory]
    [MemberData(nameof(FamilyMarkupCases))]
    public void Parser_LiftsOneCall_FromMinimalMarkup(string familyId, string markup)
    {
        var parser = FamilyRegistry.Get(familyId);
        Assert.NotNull(parser);
        Assert.Equal(familyId, parser.Id);
        Assert.True(parser.Detect(markup), $"Detect failed for {familyId}");

        var result = parser.Parse(markup, new GrammarParseContext(AllowAll));
        Assert.True(result.Calls.Count >= 1, $"Parse lifted 0 calls for {familyId}");
        Assert.Equal("search", result.Calls[0].Name);

        using var args = JsonDocument.Parse(result.Calls[0].ArgumentsJson);
        Assert.Equal("weather", args.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public void Recover_UsesFamilyOrder_AndLiftsFirstMatch()
    {
        var markup =
            """
            <|tool_calls_section_begin|>
            <|tool_call_begin|>functions.search:0<|tool_call_argument_begin|>{"query":"weather"}<|tool_call_end|>
            <|tool_calls_section_end|>
            """;

        var recovered = FamilyToolRecovery.Recover(
            text: markup,
            reasoningText: null,
            toolNames: AllowAll,
            familyIds: ["kimi"],
            hasNativeToolCalls: false);

        Assert.Single(recovered.Calls);
        Assert.Equal("search", recovered.Calls[0].Name);
        Assert.NotNull(recovered.Event);
        Assert.Equal("kimi", recovered.Event!.Family);
    }

    [Fact]
    public void AllFourteenFamilyIds_AreRegistered()
    {
        var expected = new[]
        {
            "qwen", "deepseek-r1", "dsml", "gemma4", "harmony", "mistral", "llama",
            "kimi", "glm", "granite", "minimax", "minimax-text", "olmo", "phi4"
        };

        Assert.Equal(14, expected.Length);
        Assert.Equal(14, FamilyRegistry.All.Count);
        foreach (var id in expected)
            Assert.NotNull(FamilyRegistry.Get(id));
    }
}

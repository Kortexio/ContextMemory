using System.Net;
using System.Text;
using System.Text.Json;

namespace ContextMemory.Api.Tests.Fakes;

/// <summary>
/// Stub with agentic tool-call scenarios for both Ollama native and OpenAI-compatible backends.
/// </summary>
public sealed class AgenticStubOllamaHandler : HttpMessageHandler
{
    public IReadOnlyList<HttpRequestMessage> ChatRequests => _chatRequests;
    public IReadOnlyList<string> ChatRequestBodies => _chatRequestBodies;

    private readonly List<HttpRequestMessage> _chatRequests = [];
    private readonly List<string> _chatRequestBodies = [];

    public bool InfiniteToolLoop { get; set; }
    public bool RejectFirstFinalAnswer { get; set; }

    private int _finalAnswerCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path is "/" or "")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Ollama is running", Encoding.UTF8, "text/plain")
            });
        }

        if (path.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/api/tags", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(JsonResponse(
                """{"object":"list","data":[{"id":"llama3.2","object":"model"}]}""",
                HttpStatusCode.OK));
        }

        var isOpenAiChat = path.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase);
        var isOllamaChat = path.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase);

        if (isOpenAiChat || isOllamaChat)
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";
            _chatRequests.Add(request);
            _chatRequestBodies.Add(body);

            if (IsAgentJudgePrompt(body))
            {
                var judgeJson = body.Contains("resposta-incompleta", StringComparison.OrdinalIgnoreCase)
                    ? """{"valid":false,"feedback":"A resposta não cobre o objetivo pedido. Sê mais específico."}"""
                    : """{"valid":true,"feedback":""}""";
                return Task.FromResult(isOpenAiChat
                    ? OpenAiText(judgeJson)
                    : OllamaGenerateWrapped(judgeJson));
            }

            if (IsWikiMaintainerPrompt(body))
            {
                const string wiki = """{"log_entry":"## stub","pages":[]}""";
                return Task.FromResult(isOpenAiChat ? OpenAiText(wiki) : OllamaGenerateWrapped(wiki));
            }

            // Default test app model (qwen3.5:9b/ollama) now uses client-side tool parsing
            // (see ClientSideToolCalling): native tools[] is omitted and the catalog is inlined
            // in the system prompt instead. Support both wire formats so existing scenarios
            // (native tool_calls) and the client-side JSON-in-content contract both round-trip.
            var hasNativeTools = body.Contains("\"tools\"", StringComparison.Ordinal);
            var hasClientCatalog = body.Contains("## Tool catalog", StringComparison.Ordinal);
            var hasToolResponseAlready = body.Contains("\"role\":\"tool\"", StringComparison.Ordinal)
                || body.Contains("\"role\": \"tool\"", StringComparison.Ordinal)
                || body.Contains("\"tool_call_id\"", StringComparison.Ordinal)
                || body.Contains("Tool result:", StringComparison.Ordinal);
            var useClientSideReply = isOllamaChat && hasClientCatalog && !hasNativeTools;

            var awaitingToolResult = (hasNativeTools || hasClientCatalog) && !hasToolResponseAlready;

            if (InfiniteToolLoop && (hasNativeTools || hasClientCatalog))
            {
                if (useClientSideReply)
                    return Task.FromResult(OllamaClientSideToolCall("shell_execute", """{"command":"echo loop"}"""));

                return Task.FromResult(isOpenAiChat
                    ? OpenAiToolCall("shell_execute", """{"command":"echo loop"}""")
                    : OllamaToolCall("shell_execute", """{"command":"echo loop"}"""));
            }

            if (awaitingToolResult)
            {
                var toolCall = ResolveNextToolCall(body, useClientSideReply, isOpenAiChat);
                if (toolCall is not null)
                    return Task.FromResult(toolCall);

                if (useClientSideReply)
                    return Task.FromResult(OllamaClientSideToolCall("shell_execute", """{"command":"echo agentic-ok"}"""));

                return Task.FromResult(isOpenAiChat
                    ? OpenAiToolCall("shell_execute", """{"command":"echo agentic-ok"}""")
                    : OllamaToolCall("shell_execute", """{"command":"echo agentic-ok"}"""));
            }

            var isMcpFollowUp = hasToolResponseAlready
                && (body.Contains("zuora-mcp__get_account", StringComparison.Ordinal)
                    || body.Contains("[mock:zuora-mcp]", StringComparison.Ordinal)
                    || body.Contains("MCP tool matches", StringComparison.Ordinal));

            if (isMcpFollowUp)
            {
                // Still mid discovery / call chain?
                var midChain = ResolveNextToolCall(body, useClientSideReply: isOllamaChat && hasClientCatalog && !hasNativeTools, isOpenAiChat);
                if (midChain is not null)
                    return Task.FromResult(midChain);

                const string mcpAnswer = "Conta A-001 encontrada via Zuora MCP. Estado: Active.";
                return Task.FromResult(isOpenAiChat ? OpenAiText(mcpAnswer) : OllamaText(mcpAnswer));
            }

            var content = GetFinalAnswerContent();
            return Task.FromResult(isOpenAiChat ? OpenAiText(content) : OllamaText(content));
        }

        if (path.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";

            if (IsAgentJudgePrompt(body))
            {
                if (body.Contains("resposta-incompleta", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(OllamaGenerateWrapped(
                        """{"valid":false,"feedback":"A resposta não cobre o objetivo pedido. Sê mais específico."}"""));
                }

                return Task.FromResult(OllamaGenerateWrapped("""{"valid":true,"feedback":""}"""));
            }

            return Task.FromResult(JsonResponse(
                """
                {
                  "model": "llama3.2",
                  "response": "{\"log_entry\":\"## stub\"}",
                  "done": true
                }
                """,
                HttpStatusCode.OK));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Lazy MCP: tool_search → tool_describe → zuora-mcp__get_account.
    /// Also handles shell delete / echo when not an MCP scenario.
    /// </summary>
    private static HttpResponseMessage? ResolveNextToolCall(string body, bool useClientSideReply, bool isOpenAiChat)
    {
        var wantsZuora = body.Contains("A-001", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Zuora", StringComparison.OrdinalIgnoreCase)
            || body.Contains("zuora-mcp", StringComparison.OrdinalIgnoreCase);

        if (wantsZuora)
        {
            var hasGetAccountResult = body.Contains("[mock:zuora-mcp]", StringComparison.Ordinal);
            if (hasGetAccountResult)
                return null;

            // Raw JSON bodies escape backticks as \u0060 — detect describe without relying on `.
            var hasDescribeResult =
                body.Contains("zuora-mcp__get_account", StringComparison.Ordinal)
                && (body.Contains("Input schema", StringComparison.Ordinal)
                    || body.Contains("## Parameters", StringComparison.Ordinal)
                    || body.Contains("\\u0060zuora-mcp__get_account\\u0060", StringComparison.Ordinal));
            var hasSearchResult = body.Contains("MCP tool matches", StringComparison.Ordinal);
            var getAccountInCatalog = body.Contains("zuora-mcp__get_account", StringComparison.Ordinal)
                && body.Contains("## Tool catalog", StringComparison.Ordinal);

            if (!hasSearchResult && !hasDescribeResult
                && body.Contains("tool_search", StringComparison.Ordinal))
            {
                return EmitToolCall("tool_search", """{"query":"account"}""", useClientSideReply, isOpenAiChat);
            }

            if (hasSearchResult && !hasDescribeResult
                && body.Contains("tool_describe", StringComparison.Ordinal))
            {
                return EmitToolCall(
                    "tool_describe",
                    """{"toolName":"zuora-mcp__get_account"}""",
                    useClientSideReply,
                    isOpenAiChat);
            }

            if (hasDescribeResult || getAccountInCatalog)
            {
                return EmitToolCall(
                    "zuora-mcp__get_account",
                    """{"accountId":"A-001"}""",
                    useClientSideReply,
                    isOpenAiChat);
            }
        }

        if (body.Contains("delete", StringComparison.OrdinalIgnoreCase))
        {
            return EmitToolCall(
                "shell_execute",
                """{"command":"delete --force user-test"}""",
                useClientSideReply,
                isOpenAiChat);
        }

        if (body.Contains("shell_execute", StringComparison.Ordinal)
            || body.Contains("echo agentic", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Executa echo", StringComparison.OrdinalIgnoreCase))
        {
            return EmitToolCall(
                "shell_execute",
                """{"command":"echo agentic-ok"}""",
                useClientSideReply,
                isOpenAiChat);
        }

        return null;
    }

    private static HttpResponseMessage EmitToolCall(
        string name,
        string argumentsJson,
        bool useClientSideReply,
        bool isOpenAiChat)
    {
        if (useClientSideReply)
            return OllamaClientSideToolCall(name, argumentsJson);
        return isOpenAiChat
            ? OpenAiToolCall(name, argumentsJson)
            : OllamaToolCall(name, argumentsJson);
    }

    private string GetFinalAnswerContent()
    {
        _finalAnswerCount++;
        if (RejectFirstFinalAnswer && _finalAnswerCount == 1)
            return "resposta-incompleta sem detalhe";

        return "Comando executado com sucesso. Output: agentic-ok";
    }

    private static bool IsAgentJudgePrompt(string body) =>
        body.Contains("agentic-judge", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Avalia se a resposta final", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Evaluate whether the assistant", StringComparison.OrdinalIgnoreCase);

    private static bool IsWikiMaintainerPrompt(string body) =>
        body.Contains("wiki markdown", StringComparison.OrdinalIgnoreCase)
        || body.Contains("markdown wiki", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Actualiza a wiki", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Update the markdown wiki", StringComparison.OrdinalIgnoreCase);

    private static HttpResponseMessage OpenAiText(string content) =>
        JsonResponse(
            $$"""
            {
              "id": "chatcmpl-agentic",
              "object": "chat.completion",
              "model": "llama3.2",
              "choices": [{
                "index": 0,
                "message": { "role": "assistant", "content": {{JsonSerializer.Serialize(content)}} },
                "finish_reason": "stop"
              }]
            }
            """,
            HttpStatusCode.OK);

    private static HttpResponseMessage OpenAiToolCall(string name, string arguments) =>
        JsonResponse(
            $$"""
            {
              "id": "chatcmpl-agentic-tool",
              "object": "chat.completion",
              "model": "llama3.2",
              "choices": [{
                "index": 0,
                "message": {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [{
                    "id": "call_1",
                    "type": "function",
                    "function": {
                      "name": {{JsonSerializer.Serialize(name)}},
                      "arguments": {{JsonSerializer.Serialize(arguments)}}
                    }
                  }]
                },
                "finish_reason": "tool_calls"
              }]
            }
            """,
            HttpStatusCode.OK);

    private static HttpResponseMessage OllamaText(string content) =>
        JsonResponse(
            $$"""
            {
              "model": "llama3.2",
              "message": {
                "role": "assistant",
                "content": {{JsonSerializer.Serialize(content)}}
              },
              "done": true
            }
            """,
            HttpStatusCode.OK);

    private static HttpResponseMessage OllamaToolCall(string name, string arguments) =>
        JsonResponse(
            $$"""
            {
              "model": "llama3.2",
              "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [{
                  "function": {
                    "name": {{JsonSerializer.Serialize(name)}},
                    "arguments": {{JsonSerializer.Serialize(arguments)}}
                  }
                }]
              },
              "done": true
            }
            """,
            HttpStatusCode.OK);

    /// <summary>
    /// Client-side wire format expected by <c>ClientSideToolCalling</c>: a plain assistant
    /// message whose content is a JSON tool-call payload (no native <c>tool_calls</c>).
    /// </summary>
    private static HttpResponseMessage OllamaClientSideToolCall(string name, string argumentsJson) =>
        OllamaText($$"""{"tool":{{JsonSerializer.Serialize(name)}},"arguments":{{argumentsJson}}}""");

    private static HttpResponseMessage OllamaGenerateWrapped(string innerJson) =>
        JsonResponse(
            $$"""
            {
              "model": "llama3.2",
              "response": {{JsonSerializer.Serialize(innerJson)}},
              "done": true
            }
            """,
            HttpStatusCode.OK);

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode code) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

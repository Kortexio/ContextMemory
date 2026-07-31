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

            var awaitingToolResult = body.Contains("\"tools\"", StringComparison.Ordinal)
                && !body.Contains("\"role\":\"tool\"", StringComparison.Ordinal)
                && !body.Contains("\"role\": \"tool\"", StringComparison.Ordinal)
                && !body.Contains("\"tool_call_id\"", StringComparison.Ordinal);

            if (InfiniteToolLoop && body.Contains("\"tools\"", StringComparison.Ordinal))
            {
                return Task.FromResult(isOpenAiChat
                    ? OpenAiToolCall("shell_execute", """{"command":"echo loop"}""")
                    : OllamaToolCall("shell_execute", """{"command":"echo loop"}"""));
            }

            if (awaitingToolResult)
            {
                var isMcp = body.Contains("zuora-mcp__get_account", StringComparison.Ordinal);
                if (isMcp)
                {
                    return Task.FromResult(isOpenAiChat
                        ? OpenAiToolCall("zuora-mcp__get_account", """{"accountId":"A-001"}""")
                        : OllamaToolCall("zuora-mcp__get_account", """{"accountId":"A-001"}"""));
                }

                if (body.Contains("delete", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(isOpenAiChat
                        ? OpenAiToolCall("shell_execute", """{"command":"delete --force user-test"}""")
                        : OllamaToolCall("shell_execute", """{"command":"delete --force user-test"}"""));
                }

                return Task.FromResult(isOpenAiChat
                    ? OpenAiToolCall("shell_execute", """{"command":"echo agentic-ok"}""")
                    : OllamaToolCall("shell_execute", """{"command":"echo agentic-ok"}"""));
            }

            var isMcpFollowUp = body.Contains("\"role\":\"tool\"", StringComparison.Ordinal)
                && (body.Contains("zuora-mcp__get_account", StringComparison.Ordinal)
                    || body.Contains("[mock:zuora-mcp]", StringComparison.Ordinal));

            if (isMcpFollowUp)
            {
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

using System.Net;
using System.Text;
using System.Text.Json;

namespace ContextMemory.Api.Tests.Fakes;

/// <summary>
/// Deterministic stub for Ollama native (/api/*) and OpenAI-compatible (/v1/*) backends.
/// </summary>
public sealed class StubOllamaHandler : HttpMessageHandler
{
    private const string WikiGenerateJson = """
        {
          "log_entry": "## [2026-05-27 12:00] turno | conhecimento stub",
          "pages": [
            { "path": "pages/stub-fact.md", "content": "Facto de teste persistido pelo maintainer stub." }
          ]
        }
        """;

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(JsonResponse(
                """{"object":"list","data":[{"id":"llama3.2","object":"model","created":0,"owned_by":"stub"}]}""",
                HttpStatusCode.OK));
        }

        if (path.EndsWith("/api/tags", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(JsonResponse("""{"models":[]}""", HttpStatusCode.OK));
        }

        if (path.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";
            var stream = body.Contains("\"stream\":true", StringComparison.Ordinal);

            if (stream)
            {
                const string chunk = """data: {"id":"chatcmpl-stub","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant","content":"Hi"},"finish_reason":null}]}""";
                const string done = """data: {"id":"chatcmpl-stub","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""";
                var streamBody = chunk + "\n\n" + done + "\n\ndata: [DONE]\n\n";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(streamBody, Encoding.UTF8, "text/event-stream")
                });
            }

            if (IsWikiMaintainerPrompt(body))
            {
                var wikiContent = JsonSerializer.Serialize(WikiGenerateJson);
                return Task.FromResult(JsonResponse(
                    $$"""
                    {
                      "id": "chatcmpl-wiki",
                      "object": "chat.completion",
                      "model": "llama3.2",
                      "choices": [{
                        "index": 0,
                        "message": { "role": "assistant", "content": {{wikiContent}} },
                        "finish_reason": "stop"
                      }]
                    }
                    """,
                    HttpStatusCode.OK));
            }

            return Task.FromResult(JsonResponse(
                """
                {
                  "id": "chatcmpl-stub",
                  "object": "chat.completion",
                  "model": "llama3.2",
                  "choices": [{
                    "index": 0,
                    "message": { "role": "assistant", "content": "Hello from stub" },
                    "finish_reason": "stop"
                  }]
                }
                """,
                HttpStatusCode.OK));
        }

        if (path.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";
            var stream = body.Contains("\"stream\":true", StringComparison.Ordinal);

            if (stream)
            {
                const string line = """{"model":"llama3.2","response":"Generated","done":false}""";
                const string done = """{"model":"llama3.2","response":"","done":true,"total_duration":1000,"eval_count":5}""";
                var streamBody = line + "\n" + done + "\n";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(streamBody, Encoding.UTF8, "application/x-ndjson")
                });
            }

            if (IsWikiMaintainerPrompt(body))
            {
                return Task.FromResult(JsonResponse(
                    $$"""
                    {
                      "model": "llama3.2",
                      "created_at": "2026-05-17T14:32:01Z",
                      "response": {{JsonSerializer.Serialize(WikiGenerateJson)}},
                      "done": true,
                      "done_reason": "stop",
                      "total_duration": 1000000000,
                      "eval_count": 20
                    }
                    """,
                    HttpStatusCode.OK));
            }

            return Task.FromResult(JsonResponse(
                """
                {
                  "model": "llama3.2",
                  "created_at": "2026-05-17T14:32:01Z",
                  "response": "Generated text",
                  "done": true,
                  "done_reason": "stop",
                  "total_duration": 4321000000,
                  "load_duration": 12000000,
                  "prompt_eval_count": 12,
                  "prompt_eval_duration": 280000000,
                  "eval_count": 8,
                  "eval_duration": 4000000000
                }
                """,
                HttpStatusCode.OK));
        }

        if (path.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";
            var stream = body.Contains("\"stream\":true", StringComparison.Ordinal);

            if (stream)
            {
                const string chunk = """{"model":"llama3.2","message":{"role":"assistant","content":"Hi"},"done":false}""";
                const string done = """{"model":"llama3.2","message":{"role":"assistant","content":""},"done":true,"total_duration":1000,"eval_count":2}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(chunk + "\n" + done + "\n", Encoding.UTF8, "application/x-ndjson")
                });
            }

            return Task.FromResult(JsonResponse(
                """
                {
                  "model": "llama3.2",
                  "created_at": "2026-05-17T14:32:01Z",
                  "message": {
                    "role": "assistant",
                    "content": "Hello from stub"
                  },
                  "done": true,
                  "done_reason": "stop",
                  "total_duration": 4321000000,
                  "load_duration": 12000000,
                  "prompt_eval_count": 312,
                  "prompt_eval_duration": 280000000,
                  "eval_count": 187,
                  "eval_duration": 4000000000,
                  "context": [1, 2, 3]
                }
                """,
                HttpStatusCode.OK));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static bool IsWikiMaintainerPrompt(string body) =>
        body.Contains("wiki markdown", StringComparison.OrdinalIgnoreCase)
        || body.Contains("markdown wiki", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Actualiza a wiki", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Update the markdown wiki", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Compacta a wiki", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Compact the markdown wiki", StringComparison.OrdinalIgnoreCase);

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode code) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

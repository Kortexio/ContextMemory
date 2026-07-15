using System.Net;
using System.Text;
using System.Text.Json;

namespace ContextMemory.Api.Tests.Fakes;

/// <summary>
/// Ollama stub with agentic tool-call scenario support (ACA shell + MCP).
/// </summary>
public sealed class AgenticStubOllamaHandler : HttpMessageHandler
{
    public IReadOnlyList<HttpRequestMessage> ChatRequests => _chatRequests;
    private readonly List<HttpRequestMessage> _chatRequests = [];

    /// <summary>Quando activo, responde sempre com tool_calls (para testes de timeout).</summary>
    public bool InfiniteToolLoop { get; set; }

    /// <summary>Primeira resposta final (sem tools) é rejeitada pelo LLM judge em modo hybrid.</summary>
    public bool RejectFirstFinalAnswer { get; set; }

    private int _finalAnswerCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
        {
            _chatRequests.Add(request);
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";

            var awaitingToolResult = body.Contains("\"tools\"", StringComparison.Ordinal)
                && !body.Contains("\"role\":\"tool\"", StringComparison.Ordinal)
                && !body.Contains("\"role\": \"tool\"", StringComparison.Ordinal);

            if (InfiniteToolLoop && body.Contains("\"tools\"", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(
                    """
                    {
                      "model": "llama3.2",
                      "message": {
                        "role": "assistant",
                        "content": "",
                        "tool_calls": [
                          {
                            "function": {
                              "name": "shell_execute",
                              "arguments": "{\"command\":\"echo loop\"}"
                            }
                          }
                        ]
                      },
                      "done": true
                    }
                    """,
                    HttpStatusCode.OK));
            }

            if (awaitingToolResult)
            {
                var isMcp = body.Contains("\"name\":\"zuora-mcp__get_account\"", StringComparison.Ordinal)
                    || body.Contains("\"name\": \"zuora-mcp__get_account\"", StringComparison.Ordinal);

                if (isMcp)
                {
                    return Task.FromResult(JsonResponse(
                        """
                        {
                          "model": "llama3.2",
                          "message": {
                            "role": "assistant",
                            "content": "",
                            "tool_calls": [
                              {
                                "function": {
                                  "name": "zuora-mcp__get_account",
                                  "arguments": "{\"accountId\":\"A-001\"}"
                                }
                              }
                            ]
                          },
                          "done": true
                        }
                        """,
                        HttpStatusCode.OK));
                }

                if (body.Contains("delete", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(JsonResponse(
                        """
                        {
                          "model": "llama3.2",
                          "message": {
                            "role": "assistant",
                            "content": "",
                            "tool_calls": [
                              {
                                "function": {
                                  "name": "shell_execute",
                                  "arguments": "{\"command\":\"delete --force user-test\"}"
                                }
                              }
                            ]
                          },
                          "done": true
                        }
                        """,
                        HttpStatusCode.OK));
                }

                return Task.FromResult(JsonResponse(
                    """
                    {
                      "model": "llama3.2",
                      "message": {
                        "role": "assistant",
                        "content": "",
                        "tool_calls": [
                          {
                            "function": {
                              "name": "shell_execute",
                              "arguments": "{\"command\":\"echo agentic-ok\"}"
                            }
                          }
                        ]
                      },
                      "done": true
                    }
                    """,
                    HttpStatusCode.OK));
            }

            var isMcpFollowUp = body.Contains("\"role\":\"tool\"", StringComparison.Ordinal)
                && (body.Contains("zuora-mcp__get_account", StringComparison.Ordinal)
                    || body.Contains("[mock:zuora-mcp]", StringComparison.Ordinal));

            if (isMcpFollowUp)
            {
                return Task.FromResult(JsonResponse(
                    """
                    {
                      "model": "llama3.2",
                      "message": {
                        "role": "assistant",
                        "content": "Conta A-001 encontrada via Zuora MCP. Estado: Active."
                      },
                      "done": true
                    }
                    """,
                    HttpStatusCode.OK));
            }

            var content = GetFinalAnswerContent();
            return Task.FromResult(JsonResponse(
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
                HttpStatusCode.OK));
        }

        if (path.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";

            if (IsAgentJudgePrompt(body))
            {
                if (body.Contains("resposta-incompleta", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(JsonResponse(
                        """{"valid":false,"feedback":"A resposta não cobre o objetivo pedido. Sê mais específico."}""",
                        HttpStatusCode.OK,
                        isJson: true));
                }

                return Task.FromResult(JsonResponse(
                    """{"valid":true,"feedback":""}""",
                    HttpStatusCode.OK,
                    isJson: true));
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

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode code, bool isJson = false) =>
        new(code)
        {
            Content = new StringContent(
                isJson
                    ? $$"""
                      {
                        "model": "llama3.2",
                        "response": {{System.Text.Json.JsonSerializer.Serialize(json)}},
                        "done": true
                      }
                      """
                    : json,
                Encoding.UTF8,
                "application/json")
        };
}

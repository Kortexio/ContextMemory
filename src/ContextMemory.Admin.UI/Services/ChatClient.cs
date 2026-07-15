using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMemory.Admin.UI.Models;
using ContextMemory.Core.Models;

namespace ContextMemory.Admin.UI.Services;

public sealed class ChatClient
{
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions DisplayOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly HttpClient _http;
    private readonly AdminSession _adminSession;

    public ChatClient(HttpClient http, AdminSession adminSession)
    {
        _http = http;
        _adminSession = adminSession;
    }

    public async Task<AppRuntimeConfigDto?> GetAppConfigAsync(
        ChatTestSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAppRequest(HttpMethod.Get, settings,
            $"/apps/{Uri.EscapeDataString(settings.AppId.Trim())}/config");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new AdminApiException((int)response.StatusCode, body);
        return JsonSerializer.Deserialize<AppRuntimeConfigDto>(body, WireOptions);
    }

    public async Task<AppDetailDto?> GetAppAsync(ChatTestSettings settings, CancellationToken cancellationToken = default)
    {
        using var request = CreateAppRequest(HttpMethod.Get, settings, $"/apps/{Uri.EscapeDataString(settings.AppId.Trim())}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new AdminApiException((int)response.StatusCode, body);
        return JsonSerializer.Deserialize<AppDetailDto>(body, WireOptions);
    }

    public Task<ChatExchangeResult> ChatAsync(
        ChatTestSettings settings,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var request = BuildChatRequest(settings, userMessage, stream: false);
        return SendChatAsync(settings, request, cancellationToken);
    }

    public async Task<ChatExchangeResult> ChatStreamAsync(
        ChatTestSettings settings,
        string userMessage,
        Action<ChatUiMessage> onAssistantUpdate,
        ChatUiMessage assistantMessage,
        CancellationToken cancellationToken = default)
    {
        var request = BuildChatRequest(settings, userMessage, stream: true);
        var requestJson = JsonSerializer.Serialize(request, WireOptions);
        using var httpRequest = CreateAppRequest(HttpMethod.Post, settings, "/api/chat");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var responseTimeHeader = GetHeader(response, "X-Response-Time-Ms");
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new AdminApiException((int)response.StatusCode, err);
        }

        var sb = new StringBuilder();
        string? streamMessageId = null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaResponse>(line, WireOptions);
            }
            catch
            {
                continue;
            }

            if (chunk?.ContextMemory?.Agentic is { } agenticMeta)
            {
                ApplyAgenticProgress(assistantMessage, agenticMeta);
                onAssistantUpdate(assistantMessage);
            }

            if (chunk?.Message?.Content is { Length: > 0 } part)
            {
                sb.Append(part);
                assistantMessage.FinalContent = sb.ToString();
                assistantMessage.Content = BuildAssistantDisplay(assistantMessage);
                onAssistantUpdate(assistantMessage);
            }
            else if (!string.IsNullOrEmpty(chunk?.Response))
            {
                sb.Append(chunk.Response);
                assistantMessage.FinalContent = sb.ToString();
                assistantMessage.Content = BuildAssistantDisplay(assistantMessage);
                onAssistantUpdate(assistantMessage);
            }

            if (chunk?.ContextMemory?.MessageId is { Length: > 0 } id)
                streamMessageId = id;
        }

        sw.Stop();
        var messageId = GetHeader(response, "X-Context-Memory-Message-Id") ?? streamMessageId;
        var sessionId = GetHeader(response, "X-Session-Id");
        var awaitingConfirmation = GetHeader(response, "X-Context-Memory-Agentic-Awaiting-Confirmation") == "true";
        var webSearch = ReadWebSearchHeaders(response);
        assistantMessage.MessageId = messageId;
        assistantMessage.ElapsedMs = sw.ElapsedMilliseconds;
        assistantMessage.IsStreaming = false;
        assistantMessage.AwaitingConfirmation = awaitingConfirmation || assistantMessage.AwaitingConfirmation;
        assistantMessage.Content = BuildAssistantDisplay(assistantMessage);

        return new ChatExchangeResult
        {
            Content = sb.ToString(),
            FinalContent = assistantMessage.FinalContent,
            AgenticSteps = assistantMessage.AgenticSteps,
            AwaitingConfirmation = assistantMessage.AwaitingConfirmation,
            PendingConfirmationId = assistantMessage.PendingConfirmationId,
            MessageId = messageId,
            SessionId = sessionId,
            ElapsedMs = sw.ElapsedMilliseconds,
            ResponseTimeHeaderMs = responseTimeHeader,
            WebSearchUsed = webSearch.Used,
            WebSearchProvider = webSearch.Provider,
            WebSearchSkipReason = webSearch.SkipReason,
            RawRequestJson = requestJson,
            RawResponseJson = sb.ToString(),
            Meta = null
        };
    }

    public Task<ChatExchangeResult> GenerateAsync(
        ChatTestSettings settings,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var request = BuildGenerateRequest(settings, prompt, stream: false);
        return SendGenerateAsync(settings, request, cancellationToken);
    }

    public async Task<ChatExchangeResult> GenerateStreamAsync(
        ChatTestSettings settings,
        string prompt,
        Action<ChatUiMessage> onAssistantUpdate,
        ChatUiMessage assistantMessage,
        CancellationToken cancellationToken = default)
    {
        var request = BuildGenerateRequest(settings, prompt, stream: true);
        var requestJson = JsonSerializer.Serialize(request, WireOptions);
        using var httpRequest = CreateAppRequest(HttpMethod.Post, settings, "/api/generate");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var responseTimeHeader = GetHeader(response, "X-Response-Time-Ms");
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new AdminApiException((int)response.StatusCode, err);
        }

        var sb = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaResponse>(line, WireOptions);
            }
            catch
            {
                continue;
            }

            if (chunk?.Response is { Length: > 0 } part)
            {
                sb.Append(part);
                assistantMessage.Content = sb.ToString();
                onAssistantUpdate(assistantMessage);
            }
        }

        sw.Stop();
        assistantMessage.ElapsedMs = sw.ElapsedMilliseconds;
        assistantMessage.IsStreaming = false;

        return new ChatExchangeResult
        {
            Content = sb.ToString(),
            ElapsedMs = sw.ElapsedMilliseconds,
            ResponseTimeHeaderMs = responseTimeHeader,
            RawRequestJson = requestJson,
            RawResponseJson = sb.ToString()
        };
    }

    private async Task<ChatExchangeResult> SendChatAsync(
        ChatTestSettings settings,
        OllamaRequest request,
        CancellationToken cancellationToken)
    {
        var requestJson = JsonSerializer.Serialize(request, WireOptions);
        using var httpRequest = CreateAppRequest(HttpMethod.Post, settings, "/api/chat");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var responseTimeHeader = GetHeader(response, "X-Response-Time-Ms");
        var messageId = GetHeader(response, "X-Context-Memory-Message-Id");
        var sessionId = GetHeader(response, "X-Session-Id");
        var awaitingConfirmation = GetHeader(response, "X-Context-Memory-Agentic-Awaiting-Confirmation") == "true";
        var webSearch = ReadWebSearchHeaders(response);

        if (!response.IsSuccessStatusCode)
        {
            return new ChatExchangeResult
            {
                Content = TryExtractError(responseBody),
                MessageId = messageId,
                SessionId = sessionId,
                ElapsedMs = sw.ElapsedMilliseconds,
                ResponseTimeHeaderMs = responseTimeHeader,
                WebSearchUsed = webSearch.Used,
                WebSearchProvider = webSearch.Provider,
                WebSearchSkipReason = webSearch.SkipReason,
                RawRequestJson = requestJson,
                RawResponseJson = responseBody,
                IsError = true,
                StatusCode = (int)response.StatusCode
            };
        }

        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody, WireOptions);
        var content = ollamaResponse?.Message?.Content ?? ollamaResponse?.Response ?? responseBody;
        var assistant = new ChatUiMessage
        {
            Role = "assistant",
            Content = content,
            FinalContent = content
        };

        if (ollamaResponse?.ContextMemory?.Agentic is { } agenticMeta)
        {
            ApplyAgenticProgress(assistant, agenticMeta);
            assistant.Content = BuildAssistantDisplay(assistant);
        }

        assistant.AwaitingConfirmation = awaitingConfirmation || assistant.AwaitingConfirmation;

        return new ChatExchangeResult
        {
            Content = assistant.Content,
            FinalContent = assistant.FinalContent,
            AgenticSteps = assistant.AgenticSteps,
            AwaitingConfirmation = assistant.AwaitingConfirmation,
            PendingConfirmationId = assistant.PendingConfirmationId,
            MessageId = messageId,
            SessionId = sessionId,
            ElapsedMs = sw.ElapsedMilliseconds,
            ResponseTimeHeaderMs = responseTimeHeader,
            WebSearchUsed = webSearch.Used,
            WebSearchProvider = webSearch.Provider,
            WebSearchSkipReason = webSearch.SkipReason,
            RawRequestJson = requestJson,
            RawResponseJson = PrettyJson(responseBody),
            Meta = ToMeta(ollamaResponse)
        };
    }

    private async Task<ChatExchangeResult> SendGenerateAsync(
        ChatTestSettings settings,
        OllamaGenerateRequest request,
        CancellationToken cancellationToken)
    {
        var requestJson = JsonSerializer.Serialize(request, WireOptions);
        using var httpRequest = CreateAppRequest(HttpMethod.Post, settings, "/api/generate");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var responseTimeHeader = GetHeader(response, "X-Response-Time-Ms");

        if (!response.IsSuccessStatusCode)
        {
            return new ChatExchangeResult
            {
                Content = TryExtractError(responseBody),
                ElapsedMs = sw.ElapsedMilliseconds,
                ResponseTimeHeaderMs = responseTimeHeader,
                RawRequestJson = requestJson,
                RawResponseJson = responseBody,
                IsError = true,
                StatusCode = (int)response.StatusCode
            };
        }

        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody, WireOptions);
        var content = ollamaResponse?.Response ?? ollamaResponse?.Message?.Content ?? responseBody;

        return new ChatExchangeResult
        {
            Content = content,
            ElapsedMs = sw.ElapsedMilliseconds,
            ResponseTimeHeaderMs = responseTimeHeader,
            RawRequestJson = requestJson,
            RawResponseJson = PrettyJson(responseBody),
            Meta = ToMeta(ollamaResponse)
        };
    }

    private OllamaRequest BuildChatRequest(ChatTestSettings settings, string userMessage, bool stream)
    {
        var messages = new List<OllamaMessage>();
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
            messages.Add(new OllamaMessage { Role = "system", Content = settings.SystemPrompt.Trim() });

        messages.Add(new OllamaMessage { Role = "user", Content = userMessage.Trim() });

        return new OllamaRequest
        {
            Model = settings.Model.Trim(),
            Messages = messages,
            Stream = stream,
            Format = string.IsNullOrWhiteSpace(settings.Format) ? null : settings.Format.Trim(),
            KeepAlive = string.IsNullOrWhiteSpace(settings.KeepAlive) ? null : settings.KeepAlive.Trim(),
            Options = BuildOptions(settings)
        };
    }

    private static OllamaGenerateRequest BuildGenerateRequest(ChatTestSettings settings, string prompt, bool stream) =>
        new()
        {
            Model = settings.Model.Trim(),
            Prompt = prompt.Trim(),
            Stream = stream,
            Format = string.IsNullOrWhiteSpace(settings.Format) ? null : settings.Format.Trim(),
            KeepAlive = string.IsNullOrWhiteSpace(settings.KeepAlive) ? null : settings.KeepAlive.Trim(),
            Options = BuildOptions(settings)
        };

    private static OllamaOptions BuildOptions(ChatTestSettings settings) =>
        new()
        {
            Temperature = settings.Temperature,
            TopP = settings.TopP,
            TopK = settings.TopK,
            NumCtx = settings.NumCtx,
            RepeatPenalty = settings.RepeatPenalty,
            NumPredict = settings.NumPredict
        };

    private HttpRequestMessage CreateAppRequest(HttpMethod method, ChatTestSettings settings, string path)
    {
        ValidateSettings(settings);
        var baseUrl = _adminSession.Settings.ApiBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Headers.TryAddWithoutValidation("X-App-Id", settings.AppId.Trim());
        request.Headers.TryAddWithoutValidation("X-User-Id", settings.UserId.Trim());
        if (!string.IsNullOrWhiteSpace(settings.SessionId))
            request.Headers.TryAddWithoutValidation("X-Session-Id", settings.SessionId.Trim());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        return request;
    }

    private void ValidateSettings(ChatTestSettings settings)
    {
        if (!_adminSession.IsConfigured)
            throw new InvalidOperationException("Configure API URL in Settings.");
        if (string.IsNullOrWhiteSpace(settings.AppId)
            || string.IsNullOrWhiteSpace(settings.UserId)
            || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.Model))
            throw new InvalidOperationException("AppId, UserId, API key and model are required.");
    }

    private static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static (string? Used, string? Provider, string? SkipReason) ReadWebSearchHeaders(HttpResponseMessage response) =>
        (
            GetHeader(response, "X-Web-Search-Used"),
            GetHeader(response, "X-Web-Search-Provider"),
            GetHeader(response, "X-Web-Search-Skip-Reason"));

    private static OllamaResponseMeta? ToMeta(OllamaResponse? r) =>
        r is null ? null : new OllamaResponseMeta
        {
            Model = r.Model,
            DoneReason = r.DoneReason,
            PromptEvalCount = r.PromptEvalCount,
            EvalCount = r.EvalCount,
            TotalDuration = r.TotalDuration,
            EvalDuration = r.EvalDuration
        };

    private static string TryExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString() ?? body;
        }
        catch
        {
            // ignore
        }

        return body;
    }

    private static string PrettyJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, DisplayOptions);
        }
        catch
        {
            return json;
        }
    }

    private static void ApplyAgenticProgress(ChatUiMessage message, AgenticStreamMetadata meta)
    {
        if (meta.Steps is { Count: > 0 }
            && meta.Phase is "Completed" or "TimedOut" or "MaxIterations")
        {
            message.AgenticSteps = meta.Steps.Select(s => new AgenticUiStep
            {
                Phase = "ToolCompleted",
                Label = s.Label,
                Status = s.Success ? "done" : "error",
                Iteration = s.Iteration,
                ToolName = s.ToolName
            }).ToList();
            message.AwaitingConfirmation = false;
            message.PendingConfirmationId = null;
            return;
        }

        if (meta.Steps is { Count: > 0 })
            ApplyCompletedToolSteps(message, meta.Steps);

        if (meta.AwaitingConfirmation == true || meta.Phase == "AwaitingConfirmation")
        {
            UpsertAgenticPhaseStep(message, meta, "AwaitingConfirmation", "pending");
            message.AwaitingConfirmation = true;
            message.PendingConfirmationId = meta.PendingConfirmationId
                ?? ExtractConfirmId(meta.Detail)
                ?? ExtractConfirmId(message.FinalContent);
            return;
        }

        if (string.IsNullOrWhiteSpace(meta.Label) && string.IsNullOrWhiteSpace(meta.Phase))
            return;

        var phase = meta.Phase ?? string.Empty;
        var status = ResolveStepStatus(phase);

        if (phase == "ConfirmationReceived")
        {
            UpsertAgenticPhaseStep(message, meta, phase, "done");
            message.AwaitingConfirmation = false;
            message.PendingConfirmationId = null;
            return;
        }

        if (phase == "ToolStarted")
        {
            var existing = message.AgenticSteps.LastOrDefault(s =>
                s.Phase == "ToolStarted"
                && s.ToolName == meta.ToolName
                && s.Status == "running");
            if (existing is not null)
                return;
        }

        if (phase == "ToolCompleted" && !string.IsNullOrWhiteSpace(meta.ToolName))
        {
            var running = message.AgenticSteps.LastOrDefault(s =>
                s.Phase == "ToolStarted"
                && s.ToolName == meta.ToolName
                && s.Status == "running");
            if (running is not null)
            {
                running.Phase = "ToolCompleted";
                running.Label = meta.Label ?? running.Label;
                running.Status = meta.Detail?.Contains("failed", StringComparison.OrdinalIgnoreCase) == true
                    || meta.Detail?.Contains("falhou", StringComparison.OrdinalIgnoreCase) == true
                    ? "error"
                    : "done";
                return;
            }
        }

        if (phase is "LlmRequest" or "Validating")
        {
            var running = message.AgenticSteps.LastOrDefault(s => s.Phase == phase && s.Status == "running");
            if (running is not null)
                return;
        }

        if (phase == "AwaitingConfirmation")
        {
            UpsertAgenticPhaseStep(message, meta, phase, "pending");
            message.AwaitingConfirmation = true;
            message.PendingConfirmationId = meta.PendingConfirmationId
                ?? ExtractConfirmId(meta.Detail)
                ?? ExtractConfirmId(message.FinalContent);
            return;
        }

        message.AgenticSteps.Add(new AgenticUiStep
        {
            Phase = phase,
            Label = meta.Label ?? meta.Detail ?? phase,
            Status = status,
            Iteration = meta.Iteration,
            ToolName = meta.ToolName
        });
    }

    private static void ApplyCompletedToolSteps(ChatUiMessage message, IReadOnlyList<AgenticStepSummary> steps)
    {
        message.AgenticSteps = steps.Select(s => new AgenticUiStep
        {
            Phase = "ToolCompleted",
            Label = s.Label,
            Status = s.Success ? "done" : "error",
            Iteration = s.Iteration,
            ToolName = s.ToolName
        }).ToList();
    }

    private static void UpsertAgenticPhaseStep(
        ChatUiMessage message,
        AgenticStreamMetadata meta,
        string phase,
        string status)
    {
        var existing = message.AgenticSteps.LastOrDefault(s => s.Phase == phase);
        var label = meta.Label ?? meta.Detail ?? phase;

        if (existing is not null)
        {
            existing.Label = label;
            existing.Status = status;
            existing.Iteration = meta.Iteration ?? existing.Iteration;
            existing.ToolName = meta.ToolName ?? existing.ToolName;
            return;
        }

        message.AgenticSteps.Add(new AgenticUiStep
        {
            Phase = phase,
            Label = label,
            Status = status,
            Iteration = meta.Iteration,
            ToolName = meta.ToolName
        });
    }

    private static string ResolveStepStatus(string phase) =>
        phase switch
        {
            "Completed" or "TimedOut" or "MaxIterations" => phase == "TimedOut" ? "warning" : "done",
            "ToolCompleted" or "ValidationRejected" or "ConfirmationReceived" => "done",
            "AwaitingConfirmation" => "pending",
            _ => "running"
        };

    private static string? ExtractConfirmId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        const string prefix = "[CONFIRM:";
        var start = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += prefix.Length;
        var end = text.IndexOf(']', start);
        return end > start ? text[start..end] : null;
    }

    private static string BuildAssistantDisplay(ChatUiMessage message)
    {
        if (!message.HasAgenticProgress && string.IsNullOrEmpty(message.FinalContent))
            return message.FinalContent;

        var sb = new StringBuilder();
        if (message.HasAgenticProgress)
        {
            sb.AppendLine("**Agentic**");
            foreach (var step in message.AgenticSteps)
            {
                var icon = step.Status switch
                {
                    "done" => "✓",
                    "error" => "✗",
                    "warning" => "⚠",
                    "pending" => "⏸",
                    _ => "…"
                };
                sb.AppendLine($"{icon} {step.Label}");
            }

            if (!string.IsNullOrEmpty(message.FinalContent))
                sb.AppendLine().AppendLine("**Response**");
        }

        if (!string.IsNullOrEmpty(message.FinalContent))
            sb.Append(message.FinalContent);

        return sb.ToString().TrimEnd();
    }
}

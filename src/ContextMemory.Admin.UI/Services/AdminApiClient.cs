using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Admin.UI.Models;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Admin.UI.Services;

public sealed class AdminApiClient
{
    private readonly HttpClient _http;
    private readonly AdminSession _session;
    private readonly AdminUiOptions _uiOptions;

    public AdminApiClient(HttpClient http, AdminSession session, IOptions<AdminUiOptions> uiOptions)
    {
        _http = http;
        _session = session;
        _uiOptions = uiOptions.Value;
    }

    public async Task<IReadOnlyList<AdminAppListItem>> GetAppsAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<AdminAppListItem>>("/admin/apps", cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public Task<AppStatsResponse?> GetAppStatsAsync(string appId, CancellationToken cancellationToken = default) =>
        GetAsync<AppStatsResponse>($"/admin/apps/{Uri.EscapeDataString(appId)}/stats", cancellationToken);

    public Task<AppCredentialsDto?> GetAppCredentialsAsync(string appId, CancellationToken cancellationToken = default) =>
        GetAsync<AppCredentialsDto>($"/admin/apps/{Uri.EscapeDataString(appId)}/credentials", cancellationToken);

    public async Task<AppCredentialsDto> RotateAppApiKeyAsync(string appId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/admin/apps/{Uri.EscapeDataString(appId)}/rotate-api-key");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await ReadJsonAsync<AppCredentialsDto>(response, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from rotate-api-key.");
    }

    public async Task<AppRuntimeConfigDto?> PatchConfigAsync(
        string appId,
        AppConfigPatchRequest patch,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Patch,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/config");
        request.Content = JsonContent.Create(patch);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AppRuntimeConfigDto>(response, cancellationToken).ConfigureAwait(false);
    }

    public Task<PlatformDefaultsDto?> GetPlatformDefaultsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<PlatformDefaultsDto>("/admin/platform-defaults", cancellationToken);

    public async Task<PlatformDefaultsDto?> PatchPlatformDefaultsAsync(
        PlatformDefaultsPatchRequest patch,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Patch, "/admin/platform-defaults");
        request.Content = JsonContent.Create(patch);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<PlatformDefaultsDto>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<McpServerAdminDto>> GetMcpServersAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<List<McpServerAdminDto>>(
                $"/admin/apps/{Uri.EscapeDataString(appId)}/mcp/servers",
                cancellationToken)
            .ConfigureAwait(false);
        return result ?? [];
    }

    public async Task<IReadOnlyList<McpCatalogSyncAdminDto>> RebuildMcpCatalogAsync(
        string appId,
        string? integrationName = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/mcp/catalog/rebuild");
        request.Content = JsonContent.Create(new McpCatalogSyncRequest { IntegrationName = integrationName });
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await ReadJsonAsync<List<McpCatalogSyncAdminDto>>(response, cancellationToken).ConfigureAwait(false);
        return result ?? [];
    }

    public async Task<IReadOnlyList<McpCredentialAdminDto>> GetMcpCredentialsAsync(
        string appId,
        string? integrationName = null,
        CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(integrationName)
            ? $"/admin/apps/{Uri.EscapeDataString(appId)}/mcp/credentials"
            : $"/admin/apps/{Uri.EscapeDataString(appId)}/mcp/credentials/{Uri.EscapeDataString(integrationName)}";
        var result = await GetAsync<List<McpCredentialAdminDto>>(path, cancellationToken).ConfigureAwait(false);
        return result ?? [];
    }

    public async Task UpsertMcpCredentialAsync(
        string appId,
        string integrationName,
        McpCredentialUpsertRequest payload,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/mcp/credentials/{Uri.EscapeDataString(integrationName)}");
        request.Content = JsonContent.Create(payload);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public Task<AgenticCatalogAdminDto?> GetAgenticCatalogAsync(CancellationToken cancellationToken = default) =>
        GetAsync<AgenticCatalogAdminDto>("/admin/agentic/catalog", cancellationToken);

    public Task<AgenticSkillAdminDto?> GetAgenticSkillAsync(string id, CancellationToken cancellationToken = default) =>
        GetAsync<AgenticSkillAdminDto>($"/admin/agentic/skills/{Uri.EscapeDataString(id)}", cancellationToken);

    public async Task<AgenticSkillAdminDto> UpsertAgenticSkillAsync(
        AgenticSkillAdminDto skill,
        bool create,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            create ? HttpMethod.Post : HttpMethod.Put,
            create
                ? "/admin/agentic/skills"
                : $"/admin/agentic/skills/{Uri.EscapeDataString(skill.Id)}");
        request.Content = JsonContent.Create(skill);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AgenticSkillAdminDto>(response, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty skill response.");
    }

    public async Task DeleteAgenticSkillAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"/admin/agentic/skills/{Uri.EscapeDataString(id)}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<AgenticSkillAdminDto> ImportAgenticSkillAsync(
        Stream fileStream,
        string fileName,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        content.Add(streamContent, "file", fileName);
        content.Add(new StringContent(replace ? "true" : "false"), "replace");

        using var request = CreateRequest(
            HttpMethod.Post,
            $"/admin/agentic/skills/import?replace={(replace ? "true" : "false")}");
        request.Content = content;
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AgenticSkillAdminDto>(response, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty import response.");
    }

    public string GetSkillExportUrl(string id)
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL and master key in Settings.");
        return $"{_session.Settings.ApiBaseUrl.TrimEnd('/')}/admin/agentic/skills/{Uri.EscapeDataString(id)}/export";
    }

    public async Task<AgenticGuardrailAdminDto> UpsertAgenticGuardrailAsync(
        AgenticGuardrailAdminDto guardrail,
        bool create,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            create ? HttpMethod.Post : HttpMethod.Put,
            create
                ? "/admin/agentic/guardrails"
                : $"/admin/agentic/guardrails/{Uri.EscapeDataString(guardrail.Id)}");
        request.Content = JsonContent.Create(guardrail);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AgenticGuardrailAdminDto>(response, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty guardrail response.");
    }

    public async Task DeleteAgenticGuardrailAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"/admin/agentic/guardrails/{Uri.EscapeDataString(id)}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<AgenticGuardrailAdminDto> ImportAgenticGuardrailAsync(
        Stream fileStream,
        string fileName,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        content.Add(new StringContent(replace ? "true" : "false"), "replace");
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/admin/agentic/guardrails/import?replace={(replace ? "true" : "false")}");
        request.Content = content;
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AgenticGuardrailAdminDto>(response, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty import response.");
    }

    public string GetGuardrailExportUrl(string id)
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL and master key in Settings.");
        return $"{_session.Settings.ApiBaseUrl.TrimEnd('/')}/admin/agentic/guardrails/{Uri.EscapeDataString(id)}/export";
    }

    public Task<AgenticAppCatalogAdminDto?> GetAppAgenticCatalogAsync(
        string appId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AgenticAppCatalogAdminDto>(
            $"/admin/apps/{Uri.EscapeDataString(appId)}/agentic/catalog",
            cancellationToken);

    public async Task<AgenticAppSkillAdminDto> UpsertAppSkillAsync(
        string appId,
        AgenticAppSkillAdminDto skill,
        bool create,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            create ? HttpMethod.Post : HttpMethod.Put,
            create
                ? $"/admin/apps/{Uri.EscapeDataString(appId)}/skills"
                : $"/admin/apps/{Uri.EscapeDataString(appId)}/skills/{Uri.EscapeDataString(skill.Id)}");
        request.Content = JsonContent.Create(skill);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AgenticAppSkillAdminDto>(response, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty app skill response.");
    }

    public async Task DeleteAppSkillAsync(string appId, string id, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/skills/{Uri.EscapeDataString(id)}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<AgenticAppSkillAdminDto> ImportAppSkillAsync(
        string appId,
        Stream fileStream,
        string fileName,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        content.Add(new StringContent(replace ? "true" : "false"), "replace");
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/skills/import?replace={(replace ? "true" : "false")}");
        request.Content = content;
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AgenticAppSkillAdminDto>(response, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty import response.");
    }

    public string GetAppSkillExportUrl(string appId, string id)
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL and master key in Settings.");
        return $"{_session.Settings.ApiBaseUrl.TrimEnd('/')}/admin/apps/{Uri.EscapeDataString(appId)}/skills/{Uri.EscapeDataString(id)}/export";
    }

    public async Task<AgenticAppGuardrailAdminDto> UpsertAppGuardrailAsync(
        string appId,
        AgenticAppGuardrailAdminDto guardrail,
        bool create,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            create ? HttpMethod.Post : HttpMethod.Put,
            create
                ? $"/admin/apps/{Uri.EscapeDataString(appId)}/guardrails"
                : $"/admin/apps/{Uri.EscapeDataString(appId)}/guardrails/{Uri.EscapeDataString(guardrail.Id)}");
        request.Content = JsonContent.Create(guardrail);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AgenticAppGuardrailAdminDto>(response, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty app guardrail response.");
    }

    public async Task DeleteAppGuardrailAsync(string appId, string id, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/guardrails/{Uri.EscapeDataString(id)}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<AgenticAppGuardrailAdminDto> ImportAppGuardrailAsync(
        string appId,
        Stream fileStream,
        string fileName,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        content.Add(new StringContent(replace ? "true" : "false"), "replace");
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/guardrails/import?replace={(replace ? "true" : "false")}");
        request.Content = content;
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<AgenticAppGuardrailAdminDto>(response, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty import response.");
    }

    public string GetAppGuardrailExportUrl(string appId, string id)
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL and master key in Settings.");
        return $"{_session.Settings.ApiBaseUrl.TrimEnd('/')}/admin/apps/{Uri.EscapeDataString(appId)}/guardrails/{Uri.EscapeDataString(id)}/export";
    }

    public async Task<IReadOnlyList<string>> ListLlmModelsAsync(
        string? appId = null,
        CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(appId)
            ? "/admin/models"
            : $"/admin/models?appId={Uri.EscapeDataString(appId.Trim())}";
        var payload = await GetAsync<AdminModelsListDto>(path, cancellationToken).ConfigureAwait(false);
        if (payload?.Data is null || payload.Data.Count == 0)
            return [];
        return payload.Data
            .Select(m => m.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RegisterAppResponse> RegisterAppAsync(
        RegisterAppRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "/apps/register");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await ReadJsonAsync<RegisterAppResponse>(response, cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from register endpoint.");
    }

    public async Task<HealthResponseDto?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL in Settings.");

        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        using var response = await _http.GetAsync($"{baseUrl}/health", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 503)
            await EnsureSuccessAsync(response).ConfigureAwait(false);

        return await ReadJsonAsync<HealthResponseDto>(response, cancellationToken).ConfigureAwait(false);
    }

    public string GetMetricsUrl()
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL in Settings.");

        // Prefer a browser-reachable URL (Docker maps api→localhost:5100 while server uses http://api:8080).
        var publicBase = string.IsNullOrWhiteSpace(_uiOptions.PublicApiBaseUrl)
            ? _session.Settings.ApiBaseUrl
            : _uiOptions.PublicApiBaseUrl;
        return $"{publicBase.TrimEnd('/')}/metrics";
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, AdminJson.Options, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL and master key in Settings.");

        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Settings.MasterKey);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(body)
            ? response.ReasonPhrase ?? "Request failed."
            : body;
        throw new AdminApiException((int)response.StatusCode, message);
    }
}

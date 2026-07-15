using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Admin.UI.Models;
using ContextMemory.Core.Models;

namespace ContextMemory.Admin.UI.Services;

public sealed class AdminApiClient
{
    private readonly HttpClient _http;
    private readonly AdminSession _session;

    public AdminApiClient(HttpClient http, AdminSession session)
    {
        _http = http;
        _session = session;
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

        return $"{_session.Settings.ApiBaseUrl.TrimEnd('/')}/metrics";
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

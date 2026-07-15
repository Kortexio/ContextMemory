using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ContextMemory.Infrastructure.Agentic;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic.Mcp;

public sealed class McpOAuthTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpOAuthTokenProvider> _logger;
    private readonly ConcurrentDictionary<string, CachedToken> _cache = new(StringComparer.OrdinalIgnoreCase);

    public McpOAuthTokenProvider(HttpClient httpClient, ILogger<McpOAuthTokenProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> ResolveBearerTokenAsync(
        IntegrationToolConfig server,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(server.AuthMode, "oauth-per-tenant", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(server.AuthToken) ? null : server.AuthToken;

        if (server.OAuth is not { TokenUrl.Length: > 0, ClientId.Length: > 0, ClientSecret.Length: > 0 } oauth)
        {
            return string.IsNullOrWhiteSpace(server.AuthToken) ? null : server.AuthToken;
        }

        var cacheKey = $"{server.Name}:{oauth.ClientId}:{oauth.TokenUrl}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return cached.AccessToken;

        var token = await RequestClientCredentialsTokenAsync(oauth, cancellationToken).ConfigureAwait(false);
        if (token is null)
            return string.IsNullOrWhiteSpace(server.AuthToken) ? null : server.AuthToken;

        _cache[cacheKey] = token;
        return token.AccessToken;
    }

    private async Task<CachedToken?> RequestClientCredentialsTokenAsync(
        McpOAuthConfig oauth,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, oauth.TokenUrl)
            {
                Content = new FormUrlEncodedContent(BuildTokenForm(oauth))
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OAuth token request failed HTTP {Status}: {Body}", (int)response.StatusCode, body);
                return null;
            }

            var parsed = System.Text.Json.JsonSerializer.Deserialize<OAuthTokenResponse>(body);
            if (parsed?.AccessToken is not { Length: > 0 } accessToken)
                return null;

            var expiresIn = parsed.ExpiresIn > 0 ? parsed.ExpiresIn : 3600;
            return new CachedToken
            {
                AccessToken = accessToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OAuth token request failed for client {ClientId}", oauth.ClientId);
            return null;
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildTokenForm(McpOAuthConfig oauth)
    {
        yield return new KeyValuePair<string, string>("grant_type", "client_credentials");
        yield return new KeyValuePair<string, string>("client_id", oauth.ClientId);
        yield return new KeyValuePair<string, string>("client_secret", oauth.ClientSecret);

        if (!string.IsNullOrWhiteSpace(oauth.Scope))
            yield return new KeyValuePair<string, string>("scope", oauth.Scope);

        if (!string.IsNullOrWhiteSpace(oauth.Audience))
            yield return new KeyValuePair<string, string>("audience", oauth.Audience);
    }

    private sealed class CachedToken
    {
        public required string AccessToken { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}

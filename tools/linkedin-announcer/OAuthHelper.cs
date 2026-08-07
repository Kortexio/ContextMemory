using System.Net.Http.Headers;
using System.Text.Json;

namespace LinkedInAnnouncer;

public static class OAuthHelper
{
    public static string GetAuthorizationUrl(string clientId, string redirectUri, string scopes, out string state)
    {
        state = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        return "https://www.linkedin.com/oauth/v2/authorization"
               + "?response_type=code"
               + "&client_id=" + Uri.EscapeDataString(clientId)
               + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
               + "&scope=" + Uri.EscapeDataString(scopes)
               + "&state=" + Uri.EscapeDataString(state);
    }

    public static async Task<TokenResponse> ExchangeCodeForToken(
        HttpClient httpClient,
        string code,
        string clientId,
        string clientSecret,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri
        });
        using var response = await httpClient.PostAsync(
            "https://www.linkedin.com/oauth/v2/accessToken",
            content,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed ({(int)response.StatusCode}): {body}");
        return JsonSerializer.Deserialize<TokenResponse>(body)
               ?? throw new InvalidOperationException("Empty token response.");
    }

    public static async Task<TokenResponse> RefreshToken(
        HttpClient httpClient,
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });
        using var response = await httpClient.PostAsync(
            "https://www.linkedin.com/oauth/v2/accessToken",
            content,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token refresh failed ({(int)response.StatusCode}): {body}");
        return JsonSerializer.Deserialize<TokenResponse>(body)
               ?? throw new InvalidOperationException("Empty refresh response.");
    }

    public static async Task<string> GetPersonUrn(
        HttpClient httpClient,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"userinfo failed ({(int)response.StatusCode}): {body}");
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("sub", out var sub))
            throw new InvalidOperationException("userinfo missing sub.");
        return "urn:li:person:" + sub.GetString();
    }
}

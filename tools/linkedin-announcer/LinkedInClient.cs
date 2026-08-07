using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LinkedInAnnouncer;

public static class LinkedInClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<string> PublishPost(
        HttpClient httpClient,
        string accessToken,
        string authorUrn,
        string commentary,
        CancellationToken cancellationToken = default)
    {
        var payload = new PostPayload
        {
            Author = authorUrn,
            Commentary = commentary,
            IsReshareDisabledByAuthor = false
        };

        // Pin a known-active monthly version. Auto "yyyyMM" can request a month LinkedIn has not opened yet.
        var apiVersion = Environment.GetEnvironmentVariable("LINKEDIN_API_VERSION");
        if (string.IsNullOrWhiteSpace(apiVersion))
            apiVersion = "202607";

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/posts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("LinkedIn-Version", apiVersion);
        request.Headers.TryAddWithoutValidation("X-Restli-Protocol-Version", "2.0.0");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Publish failed ({(int)response.StatusCode}): {body}");

        if (!response.Headers.TryGetValues("x-restli-id", out var ids))
            throw new InvalidOperationException("Publish succeeded but x-restli-id header missing.");

        var urn = ids.First();
        Console.WriteLine($"Published LinkedIn post: {urn}");
        return urn;
    }
}

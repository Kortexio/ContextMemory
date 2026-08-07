using System.Text.Json.Serialization;

namespace LinkedInAnnouncer;

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("refresh_token_expires_in")] int? RefreshTokenExpiresIn,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("token_type")] string? TokenType);

public sealed class Distribution
{
    [JsonPropertyName("feedDistribution")]
    public string FeedDistribution { get; set; } = "MAIN_FEED";

    [JsonPropertyName("targetEntities")]
    public object[] TargetEntities { get; set; } = [];

    [JsonPropertyName("thirdPartyDistributionChannels")]
    public object[] ThirdPartyDistributionChannels { get; set; } = [];
}

public sealed class PostPayload
{
    [JsonPropertyName("author")]
    public required string Author { get; set; }

    [JsonPropertyName("commentary")]
    public required string Commentary { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "PUBLIC";

    [JsonPropertyName("distribution")]
    public Distribution Distribution { get; set; } = new();

    [JsonPropertyName("lifecycleState")]
    public string LifecycleState { get; set; } = "PUBLISHED";

    [JsonPropertyName("isReshareDisabledByAuthor")]
    public bool IsReshareDisabledByAuthor { get; set; }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XAnnouncer;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is not ["post"])
            {
                Console.Error.WriteLine("Usage: XAnnouncer post");
                return 1;
            }

            var tag = Require("RELEASE_TAG");
            var body = Environment.GetEnvironmentVariable("RELEASE_BODY") ?? "";
            var repoUrl = Require("REPO_URL");
            var dryRun = string.Equals(Environment.GetEnvironmentVariable("DRY_RUN"), "true", StringComparison.OrdinalIgnoreCase);
            var text = TweetFormatter.Format(tag, body, repoUrl);
            Console.WriteLine(text);

            if (dryRun)
            {
                Console.WriteLine("(DRY_RUN=true — not published)");
                return 0;
            }

            var apiKey = Require("X_API_KEY");
            var apiSecret = Require("X_API_SECRET");
            var accessToken = Require("X_ACCESS_TOKEN");
            var accessSecret = Require("X_ACCESS_SECRET");

            using var http = new HttpClient();
            var urn = await XClient.PostTweetAsync(http, apiKey, apiSecret, accessToken, accessSecret, text);
            Console.WriteLine($"Published tweet id: {urn}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string Require(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException($"Missing env var {name}.");
}

public static partial class TweetFormatter
{
    public static string Format(string tagName, string releaseBody, string repoUrl)
    {
        var url = $"{repoUrl.TrimEnd('/')}/releases/tag/{tagName}";
        var firstBullet = releaseBody
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("- ") || l.StartsWith("* "));
        var proof = firstBullet is null
            ? "Agent memory you can open like a wiki."
            : Clean(firstBullet[2..]);
        if (proof.Length > 80)
            proof = proof[..77] + "…";

        var text = $"ContextMemory {tagName}\n{proof}\n{url}";
        return text.Length <= 280 ? text : text[..277] + "…";
    }

    private static string Clean(string s)
    {
        s = LinkRegex().Replace(s, "$1");
        s = BoldRegex().Replace(s, "$1");
        return s.Trim();
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldRegex();
}

public static class XClient
{
    public static async Task<string> PostTweetAsync(
        HttpClient http,
        string apiKey,
        string apiSecret,
        string accessToken,
        string accessSecret,
        string text,
        CancellationToken ct = default)
    {
        const string url = "https://api.twitter.com/2/tweets";
        var bodyJson = JsonSerializer.Serialize(new { text });
        var auth = BuildOAuth1Header("POST", url, apiKey, apiSecret, accessToken, accessSecret);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Authorization", auth);
        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"X API failed ({(int)response.StatusCode}): {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("data").GetProperty("id").GetString()
               ?? throw new InvalidOperationException("Tweet id missing.");
    }

    private static string BuildOAuth1Header(
        string method,
        string url,
        string consumerKey,
        string consumerSecret,
        string token,
        string tokenSecret)
    {
        var oauth = new SortedDictionary<string, string>
        {
            ["oauth_consumer_key"] = consumerKey,
            ["oauth_nonce"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["oauth_token"] = token,
            ["oauth_version"] = "1.0"
        };

        var paramString = string.Join("&", oauth.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var baseString = $"{method.ToUpperInvariant()}&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(paramString)}";
        var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&{Uri.EscapeDataString(tokenSecret)}";
        using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(baseString)));
        oauth["oauth_signature"] = signature;

        var header = string.Join(", ", oauth.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}=\"{Uri.EscapeDataString(kv.Value)}\""));
        return "OAuth " + header;
    }
}

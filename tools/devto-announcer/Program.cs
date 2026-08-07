using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DevToAnnouncer;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is not ["post"])
            {
                Console.Error.WriteLine("Usage: DevToAnnouncer post");
                return 1;
            }

            var tag = Require("RELEASE_TAG");
            var body = Environment.GetEnvironmentVariable("RELEASE_BODY") ?? "";
            var repoUrl = Require("REPO_URL");
            var dryRun = string.Equals(Environment.GetEnvironmentVariable("DRY_RUN"), "true", StringComparison.OrdinalIgnoreCase);
            var title = $"ContextMemory {tag}: what shipped";
            var markdown = ArticleFormatter.Format(tag, body, repoUrl);
            Console.WriteLine(title);
            Console.WriteLine(markdown);

            if (dryRun)
            {
                Console.WriteLine("(DRY_RUN=true — not published)");
                return 0;
            }

            var apiKey = Require("DEVTO_API_KEY");
            using var http = new HttpClient();
            var url = await DevToClient.PublishAsync(http, apiKey, title, markdown);
            Console.WriteLine($"Published: {url}");
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

public static class ArticleFormatter
{
    public static string Format(string tagName, string releaseBody, string repoUrl)
    {
        var notes = $"{repoUrl.TrimEnd('/')}/releases/tag/{tagName}";
        var sb = new StringBuilder();
        sb.AppendLine($"ContextMemory **{tagName}** is out.");
        sb.AppendLine();
        sb.AppendLine("Agent memory you can open like a wiki — not a vector black box, not classic RAG inject.");
        sb.AppendLine();
        sb.AppendLine("## Release notes");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(releaseBody) ? "_See GitHub release._" : releaseBody.Trim());
        sb.AppendLine();
        sb.AppendLine($"Full notes: {notes}");
        sb.AppendLine();
        sb.AppendLine("Repo: https://github.com/Kortexio/ContextMemory · Cloud: https://kortexio.io");
        return sb.ToString();
    }
}

public static class DevToClient
{
    public static async Task<string> PublishAsync(
        HttpClient http,
        string apiKey,
        string title,
        string markdownBody,
        CancellationToken ct = default)
    {
        var payload = new
        {
            article = new
            {
                title,
                published = true,
                body_markdown = markdownBody,
                tags = new[] { "dotnet", "ai", "opensource", "llm" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://dev.to/api/articles");
        request.Headers.Add("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.forem.api-v1+json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"dev.to failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("url").GetString()
               ?? throw new InvalidOperationException("dev.to response missing url.");
    }
}

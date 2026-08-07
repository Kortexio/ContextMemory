using System.Net;
using LinkedInAnnouncer;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

if (args.Length == 0)
    return Fail("Usage: LinkedInAnnouncer <get-token|post>");

using var http = new HttpClient();

try
{
    return args[0] switch
    {
        "get-token" => await RunGetTokenAsync(http),
        "post" => await RunPostAsync(http),
        _ => Fail($"Unknown mode '{args[0]}'. Use get-token or post.")
    };
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

static async Task<int> RunGetTokenAsync(HttpClient http)
{
    var clientId = RequireEnv("LINKEDIN_CLIENT_ID");
    var clientSecret = RequireEnv("LINKEDIN_CLIENT_SECRET");
    const string redirectUri = "http://localhost:8000/callback";
    var scopes = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LINKEDIN_ORG_URN"))
        ? "openid profile w_member_social"
        : "openid profile w_member_social w_organization_social";

    var url = OAuthHelper.GetAuthorizationUrl(clientId, redirectUri, scopes, out var expectedState);
    Console.WriteLine("Open this URL and authorize:");
    Console.WriteLine(url);

    using var listener = new HttpListener();
    listener.Prefixes.Add("http://localhost:8000/callback/");
    listener.Start();
    var context = await listener.GetContextAsync();
    var code = context.Request.QueryString["code"];
    var state = context.Request.QueryString["state"];
    var responseBytes = "You can close this window."u8.ToArray();
    context.Response.ContentLength64 = responseBytes.Length;
    await context.Response.OutputStream.WriteAsync(responseBytes);
    context.Response.Close();
    listener.Stop();

    if (string.IsNullOrEmpty(code))
        return Fail("Authorization callback missing code.");
    if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        return Fail("OAuth state mismatch.");

    var token = await OAuthHelper.ExchangeCodeForToken(http, code, clientId, clientSecret, redirectUri);
    var personUrn = await OAuthHelper.GetPersonUrn(http, token.AccessToken);

    Console.WriteLine();
    Console.WriteLine("Store these as GitHub Actions secrets:");
    Console.WriteLine($"LINKEDIN_ACCESS_TOKEN={token.AccessToken}");
    Console.WriteLine($"LINKEDIN_REFRESH_TOKEN={token.RefreshToken}");
    Console.WriteLine($"LINKEDIN_PERSON_URN={personUrn}");
    return 0;
}

static async Task<int> RunPostAsync(HttpClient http)
{
    var tag = RequireEnv("RELEASE_TAG");
    var body = Environment.GetEnvironmentVariable("RELEASE_BODY") ?? "";
    var repoUrl = RequireEnv("REPO_URL");
    var dryRun = string.Equals(Environment.GetEnvironmentVariable("DRY_RUN"), "true", StringComparison.OrdinalIgnoreCase);
    var orgUrn = Environment.GetEnvironmentVariable("LINKEDIN_ORG_URN");
    var author = !string.IsNullOrWhiteSpace(orgUrn) ? orgUrn! : RequireEnv("LINKEDIN_PERSON_URN");
    var refreshToken = Environment.GetEnvironmentVariable("LINKEDIN_REFRESH_TOKEN");
    var accessTokenOverride = Environment.GetEnvironmentVariable("LINKEDIN_ACCESS_TOKEN");

    var commentary = PostFormatter.FormatReleasePost(tag, body, repoUrl);
    Console.WriteLine(commentary);

    if (dryRun)
    {
        Console.WriteLine("(DRY_RUN=true — not published)");
        return 0;
    }

    string accessToken;
    if (!string.IsNullOrWhiteSpace(refreshToken))
    {
        var clientId = RequireEnv("LINKEDIN_CLIENT_ID");
        var clientSecret = RequireEnv("LINKEDIN_CLIENT_SECRET");
        var token = await OAuthHelper.RefreshToken(http, refreshToken, clientId, clientSecret);
        accessToken = token.AccessToken;
    }
    else if (!string.IsNullOrWhiteSpace(accessTokenOverride))
    {
        // Temporary path for portal-generated tokens until get-token / refresh is configured.
        Console.WriteLine("Using LINKEDIN_ACCESS_TOKEN (no refresh) — rotate to refresh token when possible.");
        accessToken = accessTokenOverride;
    }
    else
    {
        return Fail("Set LINKEDIN_REFRESH_TOKEN (preferred) or LINKEDIN_ACCESS_TOKEN.");
    }

    await LinkedInClient.PublishPost(http, accessToken, author!, commentary);
    return 0;
}

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Missing required env var {name}.");

using System.Net;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Infrastructure.Agentic;

public sealed class AgenticUrlAvailabilityChecker : IAgenticUrlAvailabilityChecker
{
    public const string HttpClientName = "UrlAvailability";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AgenticUrlAvailabilityChecker> _logger;

    public AgenticUrlAvailabilityChecker(
        IHttpClientFactory httpClientFactory,
        ILogger<AgenticUrlAvailabilityChecker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> FindUnreachableUrlsAsync(
        IReadOnlyList<string> urls,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (urls.Count == 0)
            return [];

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 500, 15_000));
        var dead = new List<string>();

        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || HostAllowlist.IsBlockedHostOrIp(uri.Host))
            {
                continue;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                using var head = new HttpRequestMessage(HttpMethod.Head, uri);
                using var headResponse = await client.SendAsync(
                        head,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token)
                    .ConfigureAwait(false);

                if (IsReachable(headResponse.StatusCode))
                    continue;

                // Some hosts reject HEAD — try GET.
                using var get = new HttpRequestMessage(HttpMethod.Get, uri);
                using var getResponse = await client.SendAsync(
                        get,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token)
                    .ConfigureAwait(false);

                if (!IsReachable(getResponse.StatusCode))
                    dead.Add(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                _logger.LogDebug(ex, "URL availability check failed for {Url}", url);
                dead.Add(url);
            }
        }

        return dead;
    }

    private static bool IsReachable(HttpStatusCode code) =>
        (int)code is >= 200 and < 400
        || code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed;
}

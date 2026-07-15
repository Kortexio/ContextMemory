using ContextMemory.Core.Localization;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Agentic;

public static class AgenticNetworkEgressPolicy
{
    public static bool IsRestricted(AppRuntimeConfig runtimeConfig) =>
        !string.Equals(
            runtimeConfig.Agentic.Guardrails.NetworkEgress,
            "allowed",
            StringComparison.OrdinalIgnoreCase)
        && !string.Equals(
            runtimeConfig.Agentic.Guardrails.NetworkEgress,
            "permissive",
            StringComparison.OrdinalIgnoreCase);

    public static bool IsIntegrationUrlAllowed(
        AppRuntimeConfig runtimeConfig,
        IntegrationToolConfig integration)
    {
        if (integration.Url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IsRestricted(runtimeConfig))
            return true;

        if (integration.AllowEgress)
            return true;

        return IsHostAllowlisted(runtimeConfig, integration.Url);
    }

    public static bool IsSandboxEndpointAllowed(
        AppRuntimeConfig runtimeConfig,
        ExecutionToolConfig execution,
        string sandboxEndpoint)
    {
        if (sandboxEndpoint.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IsRestricted(runtimeConfig))
            return true;

        if (execution.AllowEgress)
            return true;

        return IsHostAllowlisted(runtimeConfig, sandboxEndpoint);
    }

    public static bool IsAcaEndpointAllowed(
        AppRuntimeConfig runtimeConfig,
        ExecutionToolConfig execution,
        string poolEndpoint)
    {
        if (poolEndpoint.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IsRestricted(runtimeConfig))
            return true;

        if (execution.AllowEgress)
            return true;

        return IsHostAllowlisted(runtimeConfig, poolEndpoint);
    }

    private static bool IsHostAllowlisted(AppRuntimeConfig runtimeConfig, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        foreach (var allowed in runtimeConfig.Agentic.Guardrails.AllowedEgressHosts)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;

            if (uri.Host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static ToolExecutionResult BlockedResult(string target, AppRuntimeConfig config) =>
        new()
        {
            Output = AgenticMessages.NetworkEgressBlocked(target, config.DefaultLanguage),
            ExitCode = 403
        };
}

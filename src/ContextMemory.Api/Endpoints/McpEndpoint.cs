using ContextMemory.Api.Middleware;
using ContextMemory.Core.Agentic;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class McpEndpoint
{
    public static void MapMcpEndpoints(this WebApplication app)
    {
        app.MapGet("/apps/{appId}/mcp/servers", GetServersAsync);
        app.MapPost("/apps/{appId}/mcp/catalog/rebuild", RebuildCatalogAsync);
        app.MapPost("/apps/{appId}/mcp/test/{name}", TestServerAsync);
        app.MapPost("/apps/{appId}/mcp/credentials/{name}", UpsertCredentialAsync);
        app.MapGet("/admin/apps/{appId}/mcp/servers", GetServersAsync);
        app.MapPost("/admin/apps/{appId}/mcp/catalog/rebuild", RebuildCatalogAsync);
        app.MapPost("/admin/apps/{appId}/mcp/test/{name}", TestServerAsync);
        app.MapGet("/admin/apps/{appId}/mcp/credentials", ListCredentialsAsync);
        app.MapGet("/admin/apps/{appId}/mcp/credentials/{name}", GetCredentialsAsync);
        app.MapPost("/admin/apps/{appId}/mcp/credentials/{name}", UpsertCredentialAsync);
    }

    private static async Task<IResult> ListCredentialsAsync(
        string appId,
        IMcpCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var records = await credentialStore
            .ListAsync(appId, integrationName: null, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(records.Select(ToAdminDto).ToList());
    }

    private static async Task<IResult> GetCredentialsAsync(
        string appId,
        string name,
        IMcpCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var records = await credentialStore
            .ListAsync(appId, name, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(records.Select(ToAdminDto).ToList());
    }

    private static McpCredentialAdminDto ToAdminDto(Core.Agentic.Mcp.McpCredentialRecord record) =>
        new()
        {
            AppId = record.AppId,
            IntegrationName = record.IntegrationName,
            CredentialRef = record.CredentialRef,
            AuthMode = record.AuthMode,
            BearerToken = record.BearerToken,
            ApiKey = record.ApiKey,
            HeaderName = record.HeaderName,
            OAuth = record.OAuth is null
                ? null
                : new McpOAuthConfig
                {
                    TokenUrl = record.OAuth.TokenUrl,
                    ClientId = record.OAuth.ClientId,
                    ClientSecret = record.OAuth.ClientSecret,
                    Scope = record.OAuth.Scope,
                    Audience = record.OAuth.Audience
                },
            Env = record.Env,
            UpdatedAt = record.UpdatedAt
        };

    private static async Task<IResult> GetServersAsync(
        HttpContext httpContext,
        string appId,
        IAppConfigStore appConfigStore,
        IMcpToolCatalog mcpToolCatalog,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        var runtime = appConfigStore.GetConfig(appId);
        var sync = await mcpToolCatalog.SyncAsync(runtime, cancellationToken).ConfigureAwait(false);
        var syncByName = sync.ToDictionary(x => x.IntegrationName, StringComparer.OrdinalIgnoreCase);

        var servers = runtime.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Type, "mcp", StringComparison.OrdinalIgnoreCase))
            .Select(i => new
            {
                server = new McpServerInfo
                {
                    Name = i.Name,
                    Transport = i.IsStdioTransport ? "stdio" : "http",
                    Url = i.Url,
                    Command = i.Command,
                    Args = i.Args,
                    Enabled = i.Enabled,
                    Description = i.Description,
                    CredentialRef = i.CredentialRef,
                    ToolAllowlist = i.ToolAllowlist,
                    ToolDenylist = i.ToolDenylist,
                    RequiresConfirmation = i.RequiresConfirmation
                },
                sync = syncByName.TryGetValue(i.Name, out var status) ? status : null
            });

        return Results.Json(servers);
    }

    private static async Task<IResult> RebuildCatalogAsync(
        HttpContext httpContext,
        string appId,
        McpCatalogSyncRequest? request,
        IAppConfigStore appConfigStore,
        IMcpToolCatalog mcpToolCatalog,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        var runtime = FilterRuntime(appConfigStore.GetConfig(appId), request?.IntegrationName);
        var result = await mcpToolCatalog.SyncAsync(runtime, cancellationToken).ConfigureAwait(false);
        return Results.Json(result);
    }

    private static async Task<IResult> TestServerAsync(
        HttpContext httpContext,
        string appId,
        string name,
        IAppConfigStore appConfigStore,
        IMcpToolCatalog mcpToolCatalog,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        var runtime = FilterRuntime(appConfigStore.GetConfig(appId), name);
        var result = await mcpToolCatalog.SyncAsync(runtime, cancellationToken).ConfigureAwait(false);
        return Results.Json(result);
    }

    private static async Task<IResult> UpsertCredentialAsync(
        HttpContext httpContext,
        string appId,
        string name,
        McpCredentialUpsertRequest request,
        IMcpCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        if (!ValidateAppAccess(httpContext, appId, out var error))
            return error!;

        await credentialStore.UpsertAsync(
            new Core.Agentic.Mcp.McpCredentialRecord
            {
                AppId = appId,
                IntegrationName = name,
                CredentialRef = request.CredentialRef,
                AuthMode = request.AuthMode,
                BearerToken = request.BearerToken,
                ApiKey = request.ApiKey,
                HeaderName = request.HeaderName,
                OAuth = request.OAuth is null
                    ? null
                    : new Core.Agentic.Mcp.McpOAuthCredential
                    {
                        TokenUrl = request.OAuth.TokenUrl,
                        ClientId = request.OAuth.ClientId,
                        ClientSecret = request.OAuth.ClientSecret,
                        Scope = request.OAuth.Scope,
                        Audience = request.OAuth.Audience
                    },
                Env = request.Env,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static AppRuntimeConfig FilterRuntime(AppRuntimeConfig runtime, string? integrationName)
    {
        if (string.IsNullOrWhiteSpace(integrationName))
            return runtime;

        var integrations = runtime.Agentic.Tools.Integrations
            .Where(i => string.Equals(i.Name, integrationName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return runtime with
        {
            Agentic = runtime.Agentic with
            {
                Tools = runtime.Agentic.Tools with
                {
                    Integrations = integrations
                }
            }
        };
    }

    private static bool ValidateAppAccess(HttpContext httpContext, string appId, out IResult? error)
    {
        error = null;
        if (httpContext.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase))
            return true;

        var headerAppId = httpContext.Items[AuthMiddleware.AppIdItemKey] as string;

        if (!string.Equals(headerAppId, appId, StringComparison.Ordinal))
        {
            error = Results.Json(
                new { error = "X-App-Id does not match the requested appId." },
                statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        return true;
    }
}

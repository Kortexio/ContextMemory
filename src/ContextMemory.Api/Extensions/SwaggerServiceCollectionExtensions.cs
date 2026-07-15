using Microsoft.OpenApi.Models;

namespace ContextMemory.Api.Extensions;

public static class SwaggerServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemorySwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ContextMemory Middleware API",
                Version = "v1",
                Description = """
                    Ollama-compatible context middleware. Most routes require:
                    - `X-App-Id` and `X-User-Id` headers
                    - `Authorization: Bearer {app-api-key}`

                    Admin and app registration use `Authorization: Bearer {master-key}`.
                    """
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "API Key",
                In = ParameterLocation.Header,
                Description = "App API key or Master key (admin/register)."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            options.OperationFilter<ContextMemoryHeadersOperationFilter>();
        });

        return services;
    }

    public static WebApplication UseContextMemorySwagger(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "ContextMemory API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "ContextMemory API";
        });

        return app;
    }
}

internal sealed class ContextMemoryHeadersOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;
        if (!path.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("apps/", StringComparison.OrdinalIgnoreCase))
            return;

        if (path.Equals("apps/register", StringComparison.OrdinalIgnoreCase))
            return;

        operation.Parameters ??= [];

        ApplyDocumentation(operation, path);

        if (path.Equals("apps/register", StringComparison.OrdinalIgnoreCase))
            return;

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-App-Id",
            In = ParameterLocation.Header,
            Required = true,
            Schema = new OpenApiSchema { Type = "string" }
        });
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-User-Id",
            In = ParameterLocation.Header,
            Required = !path.Contains("/wiki", StringComparison.OrdinalIgnoreCase),
            Schema = new OpenApiSchema { Type = "string" }
        });
    }

    private static void ApplyDocumentation(OpenApiOperation operation, string path)
    {
        var key = path.TrimEnd('/').ToLowerInvariant();
        if (!EndpointDocs.TryGetValue(key, out var doc))
            return;

        operation.Summary = doc.Summary;
        operation.Description = doc.Description;
    }

    private static readonly Dictionary<string, (string Summary, string Description)> EndpointDocs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["api/chat"] = (
                "Chat completion",
                "Ollama-compatible chat with session wiki enrichment, optional streaming (NDJSON), web search, and agentic tools."),
            ["api/generate"] = (
                "Text generation",
                "Ollama-compatible generate endpoint (non-chat) with tenant LLM configuration."),
            ["apps/{appid}"] = (
                "Get app metadata",
                "Returns runtime metadata for the authenticated tenant."),
            ["apps/{appid}/config"] = (
                "Tenant runtime config",
                "GET returns config; PATCH updates LLM, wiki, web search, rate limits, and agentic settings."),
            ["apps/register"] = (
                "Register tenant",
                "Creates a new app and API key. Requires master key."),
            ["health"] = (
                "Health check",
                "Liveness probe: Ollama, persistence, and loaded apps."),
            ["metrics"] = (
                "Prometheus metrics",
                "Per-tenant request, wiki, web search, and agentic counters."),
            ["admin/apps"] = (
                "List apps (admin)",
                "Lists registered tenants and telemetry summary. Requires master key."),
            ["admin/apps/{appid}/stats"] = (
                "App statistics (admin)",
                "Per-app telemetry snapshot. Requires master key."),
            ["admin/apps/{appid}/config"] = (
                "Patch app config (admin)",
                "Updates tenant runtime configuration. Requires master key."),
            ["admin/apps/{appid}/credentials"] = (
                "App credentials (admin)",
                "Returns API key metadata. Requires master key."),
            ["admin/apps/{appid}/rotate-api-key"] = (
                "Rotate API key (admin)",
                "Issues a new API key for the tenant. Requires master key."),
            ["admin/sessions"] = (
                "Purge old sessions (admin)",
                "Deletes sessions older than the cutoff query parameter."),
            ["admin/sessions/{appid}/{userid}"] = (
                "Delete user sessions (admin)",
                "Deletes all sessions for a user within a tenant.")
        };
}

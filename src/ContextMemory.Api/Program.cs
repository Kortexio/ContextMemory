using ContextMemory.Api.Endpoints;
using ContextMemory.Api.Extensions;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.Configure<ContextMemoryOptions>(options =>
{
    builder.Configuration.GetSection(ContextMemoryOptions.SectionName).Bind(options);
    options.ContentRootPath = builder.Environment.ContentRootPath;
});

var adminCorsOrigins = builder.Configuration
    .GetSection($"{ContextMemoryOptions.SectionName}:AdminCorsOrigins")
    .Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminWebCors", policy =>
    {
        if (adminCorsOrigins.Length == 0)
            return;

        policy.WithOrigins(adminCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(
                "X-Session-Id",
                "X-Context-Memory-Message-Id",
                "X-Response-Time-Ms",
                "X-Web-Search-Used",
                "X-Web-Search-Provider",
                "X-Web-Search-Skip-Reason");
    });
});

builder.Services.AddContextMemory(builder.Configuration);
builder.Services.AddContextMemorySwagger();

var app = builder.Build();

await app.ApplyContextMemoryMigrationsAsync();

app.UseContextMemorySwagger();
app.UseCors("AdminWebCors");

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<AuthMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<TelemetryMiddleware>();

app.MapHealthEndpoint();
app.MapMetricsEndpoint();
app.MapChatEndpoint();
app.MapGenerateEndpoint();
app.MapAppsEndpoint();
app.MapAdminEndpoints();
app.MapAdminSessionsEndpoints();
app.MapAppsConfigEndpoints();
app.MapAppsRegisterEndpoint();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;

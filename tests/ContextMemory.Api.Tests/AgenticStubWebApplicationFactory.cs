using System.Net;
using System.Text;
using System.Text.Json;
using ContextMemory.Adapters;
using ContextMemory.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Api.Tests;

public sealed class AgenticStubWebApplicationFactory : WebApplicationFactory<Program>
{
    public AgenticStubOllamaHandler AgenticHandler { get; } = new();
    private readonly string _dataRoot;

    public AgenticStubWebApplicationFactory()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "cm-agentic-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContextMemory:PersistenceProvider"] = "File",
                ["ContextMemory:DataPath"] = _dataRoot,
                ["ContextMemory:OllamaEndpoint"] = "http://ollama-stub",
                ["ContextMemory:MasterKey"] = "test-master-key",
                ["ContextMemory:DefaultLlmModel"] = "qwen3.5:9b",
                ["ContextMemory:Apps:demo-app:ApiKey"] = "test-api-key",
                ["ContextMemory:Apps:demo-app:SystemPrompt"] = "Test persona",
                ["ContextMemory:Apps:demo-app:DefaultLanguage"] = "en-US",
                ["ContextMemory:Apps:demo-app:LlmModel"] = "qwen3.5:9b"
            });
        });

        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(OllamaAdapter)).ToList())
                services.Remove(d);

            services.AddHttpClient<OllamaAdapter>()
                .ConfigurePrimaryHttpMessageHandler(_ => AgenticHandler);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_dataRoot))
        {
            try { Directory.Delete(_dataRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }
}

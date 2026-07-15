using ContextMemory.Adapters;
using ContextMemory.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Api.Tests;

public sealed class ContextMemoryWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot;

    public ContextMemoryWebApplicationFactory()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "cm-tests", Guid.NewGuid().ToString("N"));
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
                ["ContextMemory:MasterKey"] = "test-master-key",
                ["ContextMemory:DefaultLlmModel"] = "qwen3.5:9b",
                ["ContextMemory:OllamaEndpoint"] = "http://ollama-stub",
                ["ContextMemory:Apps:demo-dev:ApiKey"] = "test-api-key",
                ["ContextMemory:Apps:demo-dev:SystemPrompt"] = "Test persona",
                ["ContextMemory:Apps:demo-dev:DefaultLanguage"] = "en-US",
                ["ContextMemory:Apps:demo-dev:LlmModel"] = "qwen3.5:9b"
            });
        });

        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(OllamaAdapter)).ToList())
                services.Remove(d);

            services.AddHttpClient<OllamaAdapter>()
                .ConfigurePrimaryHttpMessageHandler(_ => new StubOllamaHandler());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_dataRoot))
        {
            try { Directory.Delete(_dataRoot, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }
}

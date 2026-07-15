using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ContextMemory.Api.Tests;

public sealed class OllamaE2EWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_E2E_URL")
            ?? "http://localhost:11434";

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContextMemory:PersistenceProvider"] = "File",
                ["ContextMemory:OllamaEndpoint"] = ollamaUrl,
                ["ContextMemory:DataPath"] = Path.Combine(Path.GetTempPath(), "cm-ollama-e2e", Guid.NewGuid().ToString("N")),
                ["ContextMemory:MasterKey"] = "test-master-key",
                ["ContextMemory:DefaultLlmModel"] = "qwen3.5:9b",
                ["ContextMemory:Apps:demo-dev:ApiKey"] = "test-api-key",
                ["ContextMemory:Apps:demo-dev:SystemPrompt"] = "Test",
                ["ContextMemory:Apps:demo-dev:DefaultLanguage"] = "en-US",
                ["ContextMemory:Apps:demo-dev:LlmModel"] = "qwen3.5:9b"
            });
        });
    }
}

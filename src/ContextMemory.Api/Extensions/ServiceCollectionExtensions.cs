using ContextMemory.Core.Agentic;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Engine;
using ContextMemory.Core.Persistence;
using ContextMemory.Core.Profile;
using ContextMemory.Infrastructure.Profile;
using ContextMemory.Core.Session;
using ContextMemory.Adapters.WebSearch;
using ContextMemory.Core.WebSearch;
using ContextMemory.Adapters;
using ContextMemory.Api.Hosting;
using ContextMemory.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace ContextMemory.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemory(this IServiceCollection services, IConfiguration configuration)
    {
        var usePostgres = PersistenceProviders.IsPostgres(
            configuration.GetSection(ContextMemoryOptions.SectionName)["PersistenceProvider"]);

        services.Configure<OllamaAdapterOptions>(configuration.GetSection(ContextMemoryOptions.SectionName));
        services.Configure<LmStudioAdapterOptions>(configuration.GetSection(ContextMemoryOptions.SectionName));

        if (usePostgres)
            services.AddContextMemoryPersistence(configuration);
        else
            services.AddContextMemoryFilePersistence();

        services.AddContextMemoryInfrastructure();

        services.AddSingleton<IAppRegistrationService, AppRegistrationService>();

        services.AddSingleton<SessionWikiMaintainer>();
        services.AddSingleton<SessionWikiCompactor>();
        services.AddSingleton<WikiUpdateQueue>();
        services.AddSingleton<IWikiUpdateQueue>(sp => sp.GetRequiredService<WikiUpdateQueue>());
        services.AddSingleton<WikiUpdateProcessor>();
        services.AddScoped<ChatTurnContext>();
        services.AddSingleton<IWebSearchFreshnessClassifier, LlmFreshnessDetector>();
        services.AddSingleton<LlmFreshnessDetector>();
        services.AddSingleton<WebSearchFreshnessEvaluator>();
        services.AddSingleton<WebSearchEnricher>();
        services.AddSingleton<ISystemPromptBuilder, SystemPromptBuilder>();
        services.AddSingleton<IChatRequestEnricher, ChatRequestEnricher>();
        services.AddSingleton<IChatPostProcessor, ChatPostProcessor>();
        services.AddSingleton<IAgenticUsageCharger, AgenticUsageCharger>();
        services.AddScoped<IContextEngine, ContextEngine>();
        services.AddSingleton<IMessageIdTracker, MessageIdTracker>();

        services.AddSingleton<DeterministicAgentValidator>();
        services.AddSingleton<LlmJudgeAgentValidator>();
        services.AddSingleton<IAgentValidator, HybridAgentValidator>();
        services.AddSingleton<IAgentExecutionLogger, AgentExecutionLogger>();

        services.AddSingleton<IAgentConfirmationFlow, AgentConfirmationFlow>();
        services.AddSingleton<IAgentToolCallProcessor, AgentToolCallProcessor>();
        services.AddSingleton<IAgentLoopRunner, AgentLoopRunner>();
        services.AddScoped<IAgenticToolRegistry, AgenticToolRegistryService>();
        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();

        var llmTimeoutSeconds = ResolveLlmTimeoutSeconds(configuration);
        var llmTimeout = TimeSpan.FromSeconds(llmTimeoutSeconds);

        ConfigureLlmResilience(services.AddHttpClient<OllamaAdapter>(client => client.Timeout = llmTimeout), llmTimeoutSeconds);
        ConfigureLlmResilience(services.AddHttpClient<LmStudioAdapter>(client => client.Timeout = llmTimeout), llmTimeoutSeconds);
        ConfigureLlmResilience(services.AddHttpClient<OpenAiAdapter>(client => client.Timeout = llmTimeout), llmTimeoutSeconds);

        services.AddSingleton<ILlmAdapterResolver, LlmAdapterResolver>();
        services.AddContextMemoryWebSearch(configuration);

        services.AddHostedService<AppConfigBootstrapHostedService>();
        services.AddHostedService<WikiUpdateBackgroundService>();
        if (!usePostgres)
            services.AddHostedService<AppConfigWatcherHostedService>();

        return services;
    }

    private static int ResolveLlmTimeoutSeconds(IConfiguration configuration)
    {
        var configured = configuration.GetValue<int?>("ContextMemory:OllamaRequestTimeoutSeconds");
        return Math.Max(120, configured ?? 600);
    }

    private static void ConfigureLlmResilience(IHttpClientBuilder builder, int timeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 1;
            options.AttemptTimeout.Timeout = timeout;
            options.TotalRequestTimeout.Timeout = timeout;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(timeoutSeconds * 2);
        });
    }
}

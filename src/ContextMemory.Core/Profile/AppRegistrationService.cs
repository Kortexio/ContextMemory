using System.Security.Cryptography;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Exceptions;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Profile;

public sealed class AppRegistrationService : IAppRegistrationService
{
    private readonly IAppRegistry _appRegistry;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ContextMemoryOptions _options;

    public AppRegistrationService(
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options)
    {
        _appRegistry = appRegistry;
        _appConfigStore = appConfigStore;
        _options = options.Value;
    }

    public Task<RegisterAppResponse> RegisterAsync(
        RegisterAppRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.AppName) || string.IsNullOrWhiteSpace(request.Domain))
            throw new ArgumentException("appName and domain are required.");

        if (!IdentifierValidator.IsValid(request.Domain))
            throw new ArgumentException("Invalid domain format.");

        var suffix = RandomNumberGenerator.GetHexString(6, lowercase: true);
        var appId = $"{request.Domain}-prod-{suffix}";
        var apiKey = ApiKeyGenerator.CreateLiveKey();

        var profile = new AppProfile
        {
            AppId = appId,
            ApiKey = apiKey,
            DefaultLanguage = request.DefaultLanguage,
            MaxHistoryMessages = _options.MaxHistoryMessages
        };

        var record = new RegisteredAppRecord
        {
            AppId = appId,
            ApiKey = apiKey,
            AppName = request.AppName,
            Domain = request.Domain,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        if (!_appRegistry.Register(profile, record))
            throw new AppAlreadyExistsException(appId);

        var seed = new AppRuntimeConfig
        {
            AppId = appId,
            BasePersona = string.IsNullOrWhiteSpace(request.PromptPersona)
                ? "You are a helpful, clear, and precise assistant."
                : request.PromptPersona,
            FormatRules = "- Use markdown when appropriate.\n- Be clear and structured.",
            DefaultLanguage = request.DefaultLanguage,
            LlmModel = string.IsNullOrWhiteSpace(request.LlmModel) ? _options.DefaultLlmModel : request.LlmModel,
            LlmBackend = request.LlmBackend,
            MaxHistoryMessages = _options.MaxHistoryMessages
        };

        _appConfigStore.EnsureProfileExists(appId, seed);

        return Task.FromResult(new RegisterAppResponse
        {
            AppId = appId,
            ApiKey = apiKey,
            Status = "ready"
        });
    }
}

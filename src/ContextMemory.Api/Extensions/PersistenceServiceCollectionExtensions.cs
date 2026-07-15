using ContextMemory.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Api.Extensions;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemoryPersistence(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddContextMemoryPostgresPersistence(configuration);
}

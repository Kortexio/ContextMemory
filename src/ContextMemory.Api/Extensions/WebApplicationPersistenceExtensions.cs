using ContextMemory.Core.Configuration;
using ContextMemory.Core.Persistence;
using ContextMemory.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Api.Extensions;

public static class WebApplicationPersistenceExtensions
{
    public static async Task ApplyContextMemoryMigrationsAsync(this WebApplication app)
    {
        var provider = app.Configuration.GetSection(ContextMemoryOptions.SectionName)["PersistenceProvider"];
        if (!PersistenceProviders.IsPostgres(provider))
            return;

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ContextMemoryDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to apply PostgreSQL migrations. Check ConnectionStrings:ContextMemory, " +
                "ensure the server is reachable, and that the 'contextmemory' database exists.",
                ex);
        }
    }
}

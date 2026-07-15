using ContextMemory.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ContextMemory.Infrastructure.Persistence.Postgres;

public sealed class ContextMemoryDbContextFactory : IDesignTimeDbContextFactory<ContextMemoryDbContext>
{
    public ContextMemoryDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("ContextMemory");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:ContextMemory em falta. Configure appsettings.json ou variáveis de ambiente.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ContextMemoryDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(ContextMemoryDbContext).Assembly.GetName().Name));

        return new ContextMemoryDbContext(optionsBuilder.Options);
    }

    private static IConfiguration BuildConfiguration()
    {
        var basePath = Directory.GetCurrentDirectory();
        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables();

        if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
        {
            var apiPath = Path.GetFullPath(Path.Combine(basePath, "..", "ContextMemory.Api"));
            if (File.Exists(Path.Combine(apiPath, "appsettings.json")))
            {
                builder.SetBasePath(apiPath);
                builder.AddJsonFile("appsettings.json", optional: false);
                builder.AddJsonFile("appsettings.Development.json", optional: true);
            }
        }

        return builder.Build();
    }
}

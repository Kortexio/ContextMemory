namespace ContextMemory.Core.Persistence;

public static class PersistenceProviders
{
    public const string File = "File";
    public const string Postgres = "Postgres";

    public static bool IsPostgres(string? provider) =>
        string.Equals(provider, Postgres, StringComparison.OrdinalIgnoreCase);
}

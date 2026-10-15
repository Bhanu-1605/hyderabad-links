using DbUp;
using Npgsql;

namespace BusinessDirectory.Api.Data;

/// <summary>
/// Applies versioned SQL migrations (database/migrations) with DbUp, then the idempotent
/// reference seeds (database/seeds/reference). Run with:  dotnet run --project backend/BusinessDirectory.Api -- migrate
/// It is a separate command so the API itself never changes the schema on startup.
/// </summary>
public static class MigrationRunner
{
    public static int Run(IConfiguration configuration, string contentRoot)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("No connection string. Set ConnectionStrings:Default (environment variable ConnectionStrings__Default).");
            return 2;
        }

        var migrationsPath = configuration["Database:MigrationsPath"]
            ?? FindDirectory(contentRoot, "database", "migrations");
        if (migrationsPath is null || !Directory.Exists(migrationsPath))
        {
            Console.Error.WriteLine("Migrations folder not found. Set Database:MigrationsPath.");
            return 2;
        }

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsFromFileSystem(migrationsPath)
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        var seedsPath = configuration["Database:ReferenceSeedsPath"]
            ?? FindDirectory(contentRoot, "database", "seeds", "reference");
        ApplySeedFiles(connectionString, seedsPath);

        Console.WriteLine("Database is up to date.");
        return 0;
    }

    /// <summary>Loads DEV-ONLY sample data (database/seeds/dev). Refuses to run unless the environment is Development.</summary>
    public static int SeedDev(IConfiguration configuration, string contentRoot, bool isDevelopment)
    {
        if (!isDevelopment)
        {
            Console.Error.WriteLine("seed-dev only runs in the Development environment.");
            return 3;
        }

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("No connection string. Set ConnectionStrings:Default.");
            return 2;
        }

        var seedsPath = configuration["Database:DevSeedsPath"] ?? FindDirectory(contentRoot, "database", "seeds", "dev");
        if (seedsPath is null || !Directory.Exists(seedsPath))
        {
            Console.Error.WriteLine("Dev seeds folder not found.");
            return 2;
        }

        ApplySeedFiles(connectionString, seedsPath);
        Console.WriteLine("Dev sample data loaded.");
        return 0;
    }

    private static void ApplySeedFiles(string connectionString, string? seedsPath)
    {
        if (seedsPath is null || !Directory.Exists(seedsPath))
        {
            return;
        }

        using var dataSource = NpgsqlDataSource.Create(connectionString);
        foreach (var file in Directory.GetFiles(seedsPath, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var command = dataSource.CreateCommand(File.ReadAllText(file));
            command.ExecuteNonQuery();
            Console.WriteLine($"Seed applied: {Path.GetFileName(file)}");
        }
    }

    /// <summary>Walks up from <paramref name="start"/> looking for a folder made of the given segments.</summary>
    public static string? FindDirectory(string start, params string[] segments)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var candidate = Path.Combine(segments.Prepend(dir.FullName).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}

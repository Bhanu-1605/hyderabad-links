using BusinessDirectory.Api.Endpoints;
using Npgsql;
using NpgsqlTypes;

namespace BusinessDirectory.Api.Auth;

/// <summary>
/// Creates the first admin account from the command line, because there is no admin sign-up endpoint
/// (docs/security.md). Later accounts are invited by a super-admin once that surface exists.
///
///   $env:ADMIN_BOOTSTRAP_PASSWORD = "a long passphrase"
///   dotnet run --project backend/BusinessDirectory.Api -- create-admin --email you@example.com --name "Your Name" --role super_admin
///
/// The password comes from an environment variable rather than an argument, so it stays out of the shell
/// history and the process list, and it is never printed back.
/// </summary>
public static class AdminBootstrap
{
    public const string PasswordVariable = "ADMIN_BOOTSTRAP_PASSWORD";

    public static int Run(IConfiguration configuration, string[] args)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("No connection string. Set ConnectionStrings:Default (environment variable ConnectionStrings__Default).");
            return 2;
        }

        var email = AdminAuthEndpoints.NormalizeEmail(ReadOption(args, "--email"));
        var displayName = ReadOption(args, "--name")?.Trim();
        var role = (ReadOption(args, "--role") ?? "admin").Trim().ToLowerInvariant();
        var password = Environment.GetEnvironmentVariable(PasswordVariable);

        if (email is null || string.IsNullOrEmpty(displayName))
        {
            Console.Error.WriteLine("Usage: create-admin --email <address> --name \"<display name>\" [--role admin|super_admin]");
            return 2;
        }
        if (role is not ("admin" or "super_admin"))
        {
            Console.Error.WriteLine("--role must be admin or super_admin.");
            return 2;
        }
        if (!AdminPasswords.IsStrongEnough(password))
        {
            Console.Error.WriteLine($"Set {PasswordVariable} to a password of at least {AdminPasswords.MinimumLength} characters.");
            return 2;
        }

        var passwordHash = AdminPasswords.Hash(password!);

        using var dataSource = NpgsqlDataSource.Create(connectionString);
        using var conn = dataSource.OpenConnection();
        using var tx = conn.BeginTransaction();

        long adminId;
        bool created;
        using (var upsert = new NpgsqlCommand(
            @"INSERT INTO admin_users (email, display_name, password_hash, status, password_changed_at)
              VALUES (@email, @name, @hash, 'active', now())
              ON CONFLICT (email) DO UPDATE
                SET display_name = @name,
                    password_hash = @hash,
                    status = 'active',
                    password_changed_at = now(),
                    failed_login_count = 0,
                    locked_until = NULL,
                    updated_at = now()
              RETURNING id, (xmax = 0)", conn, tx))
        {
            upsert.Parameters.AddWithValue("email", email);
            upsert.Parameters.AddWithValue("name", displayName);
            upsert.Parameters.AddWithValue("hash", passwordHash);
            using var reader = upsert.ExecuteReader();
            reader.Read();
            adminId = reader.GetInt64(0);
            created = reader.GetBoolean(1);   // xmax = 0 means this row was inserted, not updated
        }

        using (var assign = new NpgsqlCommand(
            @"INSERT INTO admin_user_roles (admin_user_id, role_id)
              SELECT @admin, r.id FROM roles r WHERE r.slug = @role
              ON CONFLICT DO NOTHING", conn, tx))
        {
            assign.Parameters.AddWithValue("admin", adminId);
            assign.Parameters.AddWithValue("role", role);
            if (assign.ExecuteNonQuery() == 0 && created)
            {
                Console.Error.WriteLine($"Role '{role}' does not exist. Run the reference seeds first: npm run db:migrate");
                tx.Rollback();
                return 1;
            }
        }

        using (var audit = new NpgsqlCommand(
            @"INSERT INTO audit_logs (actor_type, action, entity_type, entity_id, details)
              VALUES ('system', @action, 'admin_user', @entity, @details)", conn, tx))
        {
            audit.Parameters.AddWithValue("action", created ? "admin.bootstrapped" : "admin.password_reset_by_cli");
            audit.Parameters.AddWithValue("entity", adminId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            audit.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb)
            {
                Value = System.Text.Json.JsonSerializer.Serialize(new { email, role }),
            });
            audit.ExecuteNonQuery();
        }

        tx.Commit();
        Console.WriteLine($"{(created ? "Created" : "Updated")} admin {email} with role {role}. Sign in at the admin site.");
        return 0;
    }

    /// <summary>Reads "--name value" from the command line. Returns null when the option is absent or has no value.</summary>
    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

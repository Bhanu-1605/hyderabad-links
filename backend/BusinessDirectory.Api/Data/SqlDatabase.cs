using Microsoft.Data.SqlClient;

namespace BusinessDirectory.Api.Data;

/// <summary>
/// The database, as every endpoint sees it: somewhere to get a command or a connection from.
///
/// It exists because Npgsql had a data-source type and Microsoft.Data.SqlClient has no
/// equivalent — SqlClient pools connections behind the connection string instead. Rather than spread
/// <c>new SqlConnection(connectionString)</c> through forty handlers, the two calls they actually
/// made (<c>CreateCommand</c> and <c>OpenConnectionAsync</c>) live here with the same names, so the
/// handlers read as they did.
///
/// Registered as a singleton. It holds a connection string, not a connection: every call opens one
/// from the pool and the caller disposes it.
/// </summary>
public sealed class SqlDatabase(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    /// <summary>
    /// A command with its own connection, opened when the command runs and closed when it is
    /// disposed. For a single statement — anything needing a transaction opens a connection instead.
    /// </summary>
    public SqlCommand CreateCommand(string sql)
    {
        // The connection is owned by the command here, which is what lets a handler write
        // `await using var command = db.CreateCommand(...)` and nothing else.
        var connection = new SqlConnection(ConnectionString);

        /* Opened here, synchronously. Npgsql's data source opened the connection on first use, so
           the forty call sites never did it themselves; SqlClient does not, and a command with a
           closed connection fails at execution rather than here.
           Synchronous Open on a pooled connection hands back an idle one without a round trip, so in
           the normal case this does not block. It can block when the pool is empty and a new
           connection has to be made -- a handler that wants to avoid that entirely uses
           OpenConnectionAsync below, which every transactional path already does. */
        connection.Open();

        var command = new SqlCommand(sql, connection);
        command.Disposed += (_, _) => connection.Dispose();
        return command;
    }

    /// <summary>An open connection from the pool, for a transaction or for several statements.</summary>
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// The SQL Server error numbers the API reacts to by name rather than by number in a condition.
///
/// PostgreSQL had a distinct SQLSTATE for each kind of violation. SQL Server does not: a unique
/// constraint and a unique index report different numbers, and a foreign key and a check constraint
/// report the *same* one, so a handler cannot tell those two apart from the error alone. Only the
/// unique case is acted on, which is the one it can tell.
/// </summary>
public static class SqlErrors
{
    /// <summary>A UNIQUE or PRIMARY KEY constraint.</summary>
    public const int UniqueConstraint = 2627;

    /// <summary>A unique index, which reports separately from a unique constraint.</summary>
    public const int UniqueIndex = 2601;

    /// <summary>True when this exception is "that row already exists", whichever form it took.</summary>
    public static bool IsUniqueViolation(SqlException exception) =>
        exception.Number is UniqueConstraint or UniqueIndex;
}

/// <summary>Small gaps between what Npgsql returned and what SqlClient returns.</summary>
public static class SqlConnectionExtensions
{
    /// <summary>
    /// Starts a transaction, typed as <see cref="SqlTransaction"/>.
    ///
    /// <c>SqlConnection.BeginTransactionAsync</c> returns the base <c>DbTransaction</c>, while
    /// Npgsql's returned its own type, and every <c>new SqlCommand(sql, conn, tx)</c> wants the
    /// concrete one. The cast is always safe — the connection is a SqlConnection — so it is done
    /// once here instead of at thirty call sites.
    /// </summary>
    public static async Task<SqlTransaction> BeginSqlTransactionAsync(
        this SqlConnection connection, CancellationToken cancellationToken) =>
        (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
}

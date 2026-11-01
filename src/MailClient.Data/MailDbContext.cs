using System.Data;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace MailClient.Data;

// Owns the SQLite connection string and the forward-only PRAGMA user_version migration chain.
// Migrations are embedded .sql resources under Migrations/, applied in order inside a transaction each.
public sealed class MailDbContext
{
    private static readonly (int Version, string ResourceName)[] Migrations =
    [
        (1, "0001_init.sql"),
        (2, "0002_fts.sql"),
        (3, "0003_fix_fts_triggers.sql"),
        (4, "0004_settings.sql"),
        (5, "0005_folder_sort_order.sql"),
        (6, "0006_mail_rules.sql"),
        (7, "0007_clear_dirty_snippets.sql"),
    ];

    private readonly string _connectionString;

    public MailDbContext(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString();
    }

    // Every repository call opens its own short-lived connection. With the default rollback journal
    // and synchronous=FULL, each single-row write was its own fsync'd transaction (a 50-header sync
    // = 50+ fsyncs) and readers blocked behind writers. The database runs in WAL mode (set once in
    // MigrateAsync; it persists in the file), and synchronous=NORMAL -- the recommended pairing for
    // WAL, which can still never corrupt the file -- is per connection, so it's applied on open.
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.StateChange += static (sender, e) =>
        {
            if (e.CurrentState != ConnectionState.Open || sender is not SqliteConnection opened)
                return;

            using var command = opened.CreateCommand();
            command.CommandText = "PRAGMA synchronous = NORMAL;";
            command.ExecuteNonQuery();
        };
        return connection;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        // Must run outside a transaction. Returns the resulting mode ("wal", or the old mode if the
        // filesystem can't do WAL) rather than failing, so there's nothing to handle.
        await using (var walCommand = connection.CreateCommand())
        {
            walCommand.CommandText = "PRAGMA journal_mode = WAL;";
            await walCommand.ExecuteScalarAsync(ct);
        }

        var currentVersion = await GetUserVersionAsync(connection, ct);

        foreach (var (version, resourceName) in Migrations)
        {
            if (version <= currentVersion)
                continue;

            await using var transaction = connection.BeginTransaction();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = LoadEmbeddedSql(resourceName);
                await command.ExecuteNonQueryAsync(ct);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"PRAGMA user_version = {version};";
                await command.ExecuteNonQueryAsync(ct);
            }

            transaction.Commit();
        }
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static string LoadEmbeddedSql(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"MailClient.Data.Migrations.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

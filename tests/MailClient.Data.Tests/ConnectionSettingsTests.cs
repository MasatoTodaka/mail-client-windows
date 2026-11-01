using Microsoft.Data.Sqlite;
using Xunit;

namespace MailClient.Data.Tests;

// Guards the write-path speedup: every repository call opens its own connection, so the database
// must be in WAL mode and each connection must come up with synchronous=NORMAL, or every
// single-row write goes back to being its own fully fsync'd rollback-journal transaction.
public sealed class ConnectionSettingsTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mailclient-test-{Guid.NewGuid():N}.db");
    private MailDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = new MailDbContext(_dbPath);
        await _db.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Database_UsesWalJournalMode()
    {
        Assert.Equal("wal", await ScalarAsync("PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task NewConnections_UseSynchronousNormal()
    {
        // 1 == NORMAL (0 = OFF, 2 = FULL, the SQLite default).
        Assert.Equal(1L, await ScalarAsync("PRAGMA synchronous;"));
    }

    [Fact]
    public async Task MigrateAsync_IsIdempotentOnAnExistingWalDatabase()
    {
        await _db.MigrateAsync();
        Assert.Equal("wal", await ScalarAsync("PRAGMA journal_mode;"));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}

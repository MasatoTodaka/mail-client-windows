using MailClient.Data;
using MailClient.Data.Search;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailClient.Data.Tests;

// M1 spike: prove FTS5 is available in this build (Microsoft.Data.Sqlite's bundled
// e_sqlite3 native lib) and that the migrations + triggers produce searchable rows.
public sealed class FtsSearchSpikeTests : IAsyncLifetime
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
        // Microsoft.Data.Sqlite pools connections per connection string, so the native
        // file handle can outlive a disposed SqliteConnection until the pool is cleared.
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Migration_CreatesFts5VirtualTable_WithoutNoSuchModuleError()
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM messages_fts;";

        var count = await command.ExecuteScalarAsync();

        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task MatchSearch_FindsDummyMessage_ByPrefixOfSubjectWord()
    {
        var accountId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        await SeedAccountAndFolderAsync(accountId, folderId);
        await SeedMessageAsync(accountId, folderId, uid: 1, subject: "Quarterly budget review", from: "finance@example.com");
        await SeedMessageAsync(accountId, folderId, uid: 2, subject: "Team lunch on Friday", from: "hr@example.com");

        var index = new FtsSearchIndex(_db);
        var results = await index.SearchAsync("budg", accountId: null, limit: 10, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Quarterly budget review", result.Message.Subject);
        Assert.Contains("budget", result.Snippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MatchSearch_ScopesToAccount_WhenAccountIdProvided()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var folderA = Guid.NewGuid();
        var folderB = Guid.NewGuid();
        await SeedAccountAndFolderAsync(accountA, folderA);
        await SeedAccountAndFolderAsync(accountB, folderB);
        await SeedMessageAsync(accountA, folderA, uid: 1, subject: "Invoice attached", from: "billing@example.com");
        await SeedMessageAsync(accountB, folderB, uid: 1, subject: "Invoice attached", from: "billing@example.com");

        var index = new FtsSearchIndex(_db);
        var results = await index.SearchAsync("invoice", accountId: accountA, limit: 10, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(accountA, result.Message.AccountId);
    }

    private async Task SeedAccountAndFolderAsync(Guid accountId, Guid folderId)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts (id, display_name, email_address, imap_host, imap_port, imap_security, imap_username, smtp_host, smtp_port, smtp_security, smtp_username)
            VALUES ($accountId, 'Test Account', 'test@example.com', 'imap.example.com', 993, 0, 'test', 'smtp.example.com', 465, 0, 'test');

            INSERT INTO folders (id, account_id, display_name, special_use)
            VALUES ($folderId, $accountId, 'Inbox', 1);
            """;
        command.Parameters.AddWithValue("$accountId", accountId.ToString());
        command.Parameters.AddWithValue("$folderId", folderId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedMessageAsync(Guid accountId, Guid folderId, uint uid, string subject, string from)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO messages (id, account_id, folder_id, uid, subject, from_display, from_address, to_recipients, date)
            VALUES ($id, $accountId, $folderId, $uid, $subject, $from, $from, 'me@example.com', $date);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$accountId", accountId.ToString());
        command.Parameters.AddWithValue("$folderId", folderId.ToString());
        command.Parameters.AddWithValue("$uid", (long)uid);
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }
}

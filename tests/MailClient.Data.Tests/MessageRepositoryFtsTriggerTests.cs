using MailClient.Core.Models;
using MailClient.Data.Repositories;
using MailClient.Data.Search;
using Xunit;

namespace MailClient.Data.Tests;

// Regression test for a real M4 bug: the messages_fts_au/messages_fts_ad triggers used FTS5's
// external-content-only 'delete' special command against a standalone FTS5 table, which threw
// "SQLite Error 1: 'SQL logic error'" on every message UPDATE or DELETE (see migration 0003).
public sealed class MessageRepositoryFtsTriggerTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mailclient-test-{Guid.NewGuid():N}.db");
    private MailDbContext _db = null!;
    private Guid _accountId;
    private Guid _folderId;

    public async Task InitializeAsync()
    {
        _db = new MailDbContext(_dbPath);
        await _db.MigrateAsync();

        _accountId = Guid.NewGuid();
        _folderId = Guid.NewGuid();

        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts (id, display_name, email_address, imap_host, imap_port, imap_security, imap_username, smtp_host, smtp_port, smtp_security, smtp_username)
            VALUES ($accountId, 'Test', 'test@example.com', 'imap.example.com', 993, 0, 'test', 'smtp.example.com', 465, 0, 'test');

            INSERT INTO folders (id, account_id, display_name, special_use)
            VALUES ($folderId, $accountId, 'Inbox', 1);
            """;
        command.Parameters.AddWithValue("$accountId", _accountId.ToString());
        command.Parameters.AddWithValue("$folderId", _folderId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UpdatingAMessage_DoesNotThrow_AndFtsReflectsTheNewSubject()
    {
        var messageStore = new MessageRepository(_db);
        var messageId = Guid.NewGuid();

        await messageStore.SaveAsync(NewMessage(messageId, uid: 1, subject: "Original subject"), default);

        await messageStore.SaveAsync(NewMessage(messageId, uid: 1, subject: "Updated subject"), default);

        var index = new FtsSearchIndex(_db);
        var oldResults = await index.SearchAsync("Original", accountId: null, limit: 10, default);
        var newResults = await index.SearchAsync("Updated", accountId: null, limit: 10, default);
        Assert.Empty(oldResults);
        Assert.Single(newResults);
    }

    [Fact]
    public async Task DeletingAMessage_DoesNotThrow_AndRemovesItFromFts()
    {
        var messageStore = new MessageRepository(_db);
        var messageId = Guid.NewGuid();
        await messageStore.SaveAsync(NewMessage(messageId, uid: 1, subject: "Disappearing message"), default);

        await messageStore.DeleteAsync(messageId, default);

        var index = new FtsSearchIndex(_db);
        var results = await index.SearchAsync("Disappearing", accountId: null, limit: 10, default);
        Assert.Empty(results);
    }

    private MailMessage NewMessage(Guid id, uint uid, string subject) => new()
    {
        Id = id,
        AccountId = _accountId,
        FolderId = _folderId,
        Uid = uid,
        Subject = subject,
        FromDisplay = "Sender",
        FromAddress = "sender@example.com",
        ToRecipients = "me@example.com",
        Date = DateTimeOffset.UtcNow,
    };
}

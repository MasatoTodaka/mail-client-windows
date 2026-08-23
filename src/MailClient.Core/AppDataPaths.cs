namespace MailClient.Core;

// Resolved once (in MailClient.Infrastructure) and shared via DI so every consumer agrees on
// where local app data lives. Packaged (MSIX) apps have observed Environment.GetFolderPath(
// SpecialFolder.LocalApplicationData) resolve inconsistently between call sites — resolving it
// once here and passing the result around avoids that class of bug.
public sealed class AppDataPaths(string rootDirectory)
{
    public string RootDirectory { get; } = rootDirectory;
    public string DatabasePath => Path.Combine(RootDirectory, "mailclient.db");
    public string BodiesDirectory => Path.Combine(RootDirectory, "Bodies");
    public string OutboxDirectory => Path.Combine(RootDirectory, "Outbox");
    public string LogosDirectory => Path.Combine(RootDirectory, "Logos");
}

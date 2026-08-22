using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public sealed record SearchResult(MailMessage Message, string Snippet);

public interface ISearchIndex
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, Guid? accountId, int limit, CancellationToken ct);
}

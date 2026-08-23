using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MailClient.Core.Models;

public sealed class MailMessage : INotifyPropertyChanged
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid FolderId { get; init; }

    // IMAP UID within the folder; only meaningful together with the folder's UidValidity.
    public required uint Uid { get; set; }
    public string? MessageId { get; set; } // RFC Message-Id header, for dedupe/threading
    public string? InReplyTo { get; set; }
    public string? References { get; set; }

    public required string Subject { get; set; }
    public required string FromDisplay { get; set; }
    public required string FromAddress { get; set; }
    public required string ToRecipients { get; set; } // ';'-joined address list
    public string? CcRecipients { get; set; }

    public required DateTimeOffset Date { get; set; }
    public string Snippet { get; set; } = string.Empty;

    // MessageActionService mutates these two in place on the exact instance a message list row
    // is bound to (rather than only writing the DB and waiting for a reload), so it can give
    // immediate optimistic feedback. Notifying lets MessageListView's Mode=OneWay bindings (the
    // unread dot, the flag icon, the sender/subject font weight) pick that up directly — the row's
    // ListViewItem container is never replaced, so a read/flag toggle can't disturb its selection
    // highlight or trigger WinUI's item-reveal transition. Every other property here is still
    // effectively load-once (OneTime x:Bind) since nothing mutates them on a live instance.
    private bool _isRead;
    public bool IsRead
    {
        get => _isRead;
        set => SetField(ref _isRead, value);
    }

    private bool _isFlagged;
    public bool IsFlagged
    {
        get => _isFlagged;
        set => SetField(ref _isFlagged, value);
    }

    public bool IsAnswered { get; set; }
    public bool IsDraft { get; set; }
    public bool HasAttachments { get; set; }
    public int Size { get; set; }

    public bool IsBodyDownloaded { get; set; }
    public string? BodyTextPath { get; set; } // large bodies cached as files, not DB blobs
    public string? BodyHtmlPath { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

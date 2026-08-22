namespace MailClient.Core.Abstractions;

// Translated from MailKit's own exception types by MailClient.Mail, so that consumers
// (ViewModels, etc.) can react to connect/auth failures without depending on MailKit directly.
public sealed class ImapAuthenticationException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class ImapConnectionException(string message, Exception? inner = null)
    : Exception(message, inner);

namespace MailClient.Core.Abstractions;

// Translated from MailKit's own exception types by MailClient.Mail, so that consumers
// (ViewModels, etc.) can react to SMTP connect/auth failures without depending on MailKit directly.
public sealed class SmtpAuthenticationException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class SmtpConnectionException(string message, Exception? inner = null)
    : Exception(message, inner);

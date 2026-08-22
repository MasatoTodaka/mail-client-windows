namespace MailClient.Core.Abstractions;

// JSON payload shape stored in OutboxAction.PayloadJson for SendMessage actions: the on-disk
// path of the fully-formed .eml (RFC 5322) file to hand to ISmtpSender once it exists (M7).
public sealed record OutboxSendPayload(string EmlFilePath);

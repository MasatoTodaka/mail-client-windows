using System.Text;

namespace MailClient.Mail.Imap;

// Some senders (seen from Japanese financial institutions) put raw ISO-2022-JP bytes directly in
// the Subject header without RFC 2047 encoded-word wrapping (no `=?ISO-2022-JP?B?...?=`). Since
// ISO-2022-JP is strictly 7-bit, MimeKit's UTF-8-first header decoder "succeeds" -- every byte is
// already valid UTF-8 -- so it reproduces the raw bytes as literal characters, ESC (0x1B) shift
// sequences included, instead of decoding them. MimeKit's ParserOptions.CharsetEncoding fallback
// only kicks in when UTF-8 decoding fails, so it can't help here; this re-derives the original
// bytes from the (mis-)decoded string and re-decodes them as ISO-2022-JP instead.
internal static class SubjectCharsetFixer
{
    // ESC -- ISO-2022-JP's shift-sequence lead byte. It should never legitimately appear in a
    // decoded subject, so its presence is a reliable signal that decoding went wrong.
    private const char EscapeChar = (char)0x1B;

    static SubjectCharsetFixer() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Fix(string subject)
    {
        if (subject.IndexOf(EscapeChar) < 0)
            return subject;

        // Only safe when every character is a literal 7-bit byte reproduced by the decode above --
        // otherwise this isn't the raw-ISO-2022-JP case and re-encoding would corrupt real text.
        if (subject.Any(c => c > 0x7F))
            return subject;

        try
        {
            var bytes = new byte[subject.Length];
            for (var i = 0; i < subject.Length; i++)
                bytes[i] = (byte)subject[i];

            return Encoding.GetEncoding("iso-2022-jp").GetString(bytes);
        }
        catch
        {
            return subject; // best-effort -- leave the mojibake rather than throw
        }
    }
}

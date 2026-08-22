using System.Text;

namespace MailClient.Core.Text;

// Some senders (seen from Japanese financial institutions) put raw ISO-2022-JP bytes directly in
// message text -- subject headers without RFC 2047 encoded-word wrapping (no
// `=?ISO-2022-JP?B?...?=`), and message bodies without a matching (or any) declared charset.
// Since ISO-2022-JP is strictly 7-bit, MimeKit's UTF-8-first decoder "succeeds" -- every byte is
// already valid UTF-8 -- so it reproduces the raw bytes as literal characters, ESC (0x1B) shift
// sequences included, instead of decoding them. MimeKit's ParserOptions.CharsetEncoding fallback
// only kicks in when UTF-8 decoding fails, so it can't help here; this re-derives the original
// bytes from the (mis-)decoded string and re-decodes them as ISO-2022-JP instead. Works equally
// on a whole HTML body: ISO-2022-JP's shift sequences are stateful and self-delimiting, so a
// document alternating between ASCII markup and shifted-in Japanese text round-trips correctly.
//
// Lives in Core (not Mail) so MailClient.Data can also run it as a one-time local backfill over
// content that was cached before this fix existed, without a Data -> Mail dependency.
public static class MojibakeFixer
{
    // ESC -- ISO-2022-JP's shift-sequence lead byte. It should never legitimately appear in
    // correctly-decoded text, so its presence is a reliable signal that decoding went wrong.
    private const char EscapeChar = (char)0x1B;

    static MojibakeFixer() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Fix(string text)
    {
        if (text.IndexOf(EscapeChar) < 0)
            return text;

        // Only safe when every character is a literal 7-bit byte reproduced by the decode above --
        // otherwise this isn't the raw-ISO-2022-JP case and re-encoding would corrupt real text.
        if (text.Any(c => c > 0x7F))
            return text;

        try
        {
            var bytes = new byte[text.Length];
            for (var i = 0; i < text.Length; i++)
                bytes[i] = (byte)text[i];

            return Encoding.GetEncoding("iso-2022-jp").GetString(bytes);
        }
        catch
        {
            return text; // best-effort -- leave the mojibake rather than throw
        }
    }
}

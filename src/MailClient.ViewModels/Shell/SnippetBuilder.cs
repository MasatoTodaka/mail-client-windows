using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace MailClient.ViewModels.Shell;

// One- or two-line preview text for the message list, derived from a message body once it is known.
public static class SnippetBuilder
{
    private const int MaxLength = 160;
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // Marketing mail pads its hidden preheader with zero-width characters and sometimes
    // double-encoded entities (&zwnj; &#847;); neither belongs in a visible preview.
    private static readonly Regex LeftoverEntity = new(@"&#?[A-Za-z0-9]{1,10};", RegexOptions.Compiled);
    // Bare links (tracking URLs in a header or footer) make a useless preview.
    private static readonly Regex Url = new(@"https?://S+", RegexOptions.Compiled);
    private static readonly Regex Invisible = new(@"[­͏​-‏  ⁠-⁯﻿]", RegexOptions.Compiled);

    public static string From(string? plainText, string? html)
    {
        var source = !string.IsNullOrWhiteSpace(plainText) ? plainText : HtmlToText(html);
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var cleaned = Invisible.Replace(LeftoverEntity.Replace(Url.Replace(source, " "), " "), string.Empty);
        var collapsed = Whitespace.Replace(cleaned, " ").Trim();
        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength];
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var noise = doc.DocumentNode.SelectNodes("//script|//style|//head");
        if (noise is not null)
        {
            foreach (var node in noise.ToList())
                node.Remove();
        }

        // Join text nodes with a space: InnerText would run adjacent blocks together ("...ChipsTonight").
        var texts = doc.DocumentNode.DescendantsAndSelf()
            .Where(n => n.NodeType == HtmlNodeType.Text)
            .Select(n => HtmlEntity.DeEntitize(n.InnerText));
        return string.Join(' ', texts);
    }
}

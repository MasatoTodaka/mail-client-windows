using HtmlAgilityPack;

namespace MailClient.ViewModels.Shell;

// Minimal HTML-email sanitizer: strips script/event-handler execution vectors (WebView2 renders
// this in a real Chromium engine, so unsanitized mail HTML is a real XSS surface) and, unless
// explicitly allowed, blocks remote <img> loads — the classic tracking-pixel vector, matching
// macOS Mail's default "block remote content" behavior.
internal static class HtmlSanitizer
{
    private static readonly string[] DangerousTags = ["script", "iframe", "object", "embed", "link", "meta", "base"];

    public static (string Html, bool BlockedRemoteImages) Sanitize(string rawHtml, bool allowRemoteImages)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(rawHtml);

        foreach (var tag in DangerousTags)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes is null)
                continue;
            foreach (var node in nodes)
                node.Remove();
        }

        foreach (var node in doc.DocumentNode.DescendantsAndSelf().ToList())
        {
            if (node.Attributes is null)
                continue;

            foreach (var attr in node.Attributes.Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ToList())
                node.Attributes.Remove(attr);

            var href = node.Attributes["href"];
            if (href is not null && href.Value.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                href.Remove();
        }

        var blockedAny = false;
        var images = doc.DocumentNode.SelectNodes("//img[@src]");
        if (images is not null)
        {
            foreach (var img in images)
            {
                var src = img.GetAttributeValue("src", string.Empty);
                if (!IsRemote(src))
                    continue;

                blockedAny = true;
                if (!allowRemoteImages)
                    img.SetAttributeValue("src", string.Empty);
            }
        }

        return (doc.DocumentNode.OuterHtml, blockedAny);
    }

    private static bool IsRemote(string src) =>
        Uri.TryCreate(src, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

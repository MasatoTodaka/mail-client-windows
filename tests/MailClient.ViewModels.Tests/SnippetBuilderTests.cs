using MailClient.ViewModels.Shell;
using Xunit;

namespace MailClient.ViewModels.Tests;

public sealed class SnippetBuilderTests
{
    [Fact]
    public void PlainText_CollapsesWhitespaceAndNewlines()
    {
        Assert.Equal("お疲れさまです。 資料を共有しました。", SnippetBuilder.From("お疲れさまです。\r\n\r\n   資料を共有しました。\n", null));
    }

    [Fact]
    public void PlainTextWinsOverHtml_WhenBothExist()
    {
        Assert.Equal("text part", SnippetBuilder.From("text part", "<p>html part</p>"));
    }

    [Fact]
    public void HtmlOnly_StripsTagsStylesAndScripts_AndDecodesEntities()
    {
        var html = "<html><head><title>T</title><style>p{color:red}</style></head><body><script>x()</script><p>Fish &amp; Chips</p><p>Tonight</p></body></html>";
        Assert.Equal("Fish & Chips Tonight", SnippetBuilder.From(null, html));
    }

    [Fact]
    public void LongBodies_AreTruncated()
    {
        Assert.Equal(160, SnippetBuilder.From(new string('あ', 1000), null).Length);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   \n  ", "<html><body>  </body></html>")]
    public void NothingReadable_YieldsEmpty(string? text, string? html)
    {
        Assert.Equal(string.Empty, SnippetBuilder.From(text, html));
    }
}

using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Notices;

/// <summary>The app notice text check: Telegram's HTML subset passes, anything Telegram would refuse is named before sending.</summary>
public sealed class TelegramHtmlTests
{
    [Theory]
    [InlineData("Plain text, no tags.")]
    [InlineData("<b>Bold</b>, <strong>strong</strong>, <i>i</i>, <em>em</em>, <u>u</u>, <ins>ins</ins>, <s>s</s>, <strike>x</strike>, <del>d</del>")]
    [InlineData("<a href=\"https://events.example.test/e/1?a=1&amp;b=2\">Details</a> &lt;3 &gt; &amp; &quot; &#169; &#x1F600;")]
    [InlineData("<span class=\"tg-spoiler\">hidden</span> <tg-spoiler>also</tg-spoiler> <tg-emoji emoji-id=\"5368324170671202286\">👍</tg-emoji>")]
    [InlineData("<pre><code class=\"language-csharp\">var a = 1;</code></pre> <code>x</code>")]
    [InlineData("<blockquote>quote</blockquote><blockquote expandable>long</blockquote>")]
    [InlineData("<b><i>nested</i></b>\nnew line")]
    public void FindProblem_ShouldPassTheSubset(string text) => TelegramHtml.FindProblem(text).Should().BeNull();

    [Theory]
    [InlineData("<b>unclosed", "never closed")]
    [InlineData("<b><i>crossed</b></i>", "matches no open tag")]
    [InlineData("</b>", "matches no open tag")]
    [InlineData("<div>block</div>", "no tag <div>")]
    [InlineData("<br>", "no tag <br>")]
    [InlineData("<b class=\"x\">b</b>", "takes no attributes")]
    [InlineData("<a>no href</a>", "one href")]
    [InlineData("<span>plain</span>", "tg-spoiler")]
    [InlineData("1 < 2", "opens no tag")]
    [InlineData("2 > 1", "outside a tag")]
    [InlineData("Tom & Jerry", "starts no entity")]
    [InlineData("&nbsp;", "starts no entity")]
    public void FindProblem_ShouldNameWhatTelegramWouldRefuse(string text, string problem)
        => TelegramHtml.FindProblem(text).Should().Contain(problem);
}

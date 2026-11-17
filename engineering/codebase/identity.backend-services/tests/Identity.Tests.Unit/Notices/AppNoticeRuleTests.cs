using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Notices;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Notices;

/// <summary>The rules an app notice's body keeps before anything is claimed or sent.</summary>
public sealed class AppNoticeRuleTests
{
    private static readonly AppNotice Valid = new()
    {
        Subject = "01900000-0000-7000-8000-000000000001",
        Text = "<b>Fashion week</b> opens on Monday.",
        Buttons = [new AppNoticeButton { Text = "Open", Url = "https://events.example.test/e/1" }],
        IdempotencyKey = "digest-1:01900000",
        Culture = "uz-Latn",
    };

    [Fact]
    public void FindProblem_ShouldPassAValidNotice() => AppNoticeService.FindProblem(Valid).Should().BeNull();

    [Fact]
    public void FindProblem_ShouldNameEachBrokenRule()
    {
        AppNoticeService.FindProblem(Valid with { Subject = "not-a-uuid" }).Should().Contain("sub");
        AppNoticeService.FindProblem(Valid with { Text = " " }).Should().Contain("text is required");
        AppNoticeService.FindProblem(Valid with { Text = new string('a', AppNoticeConstants.MaxTextLength + 1) }).Should().Contain("at most");
        AppNoticeService.FindProblem(Valid with { Text = "a < b" }).Should().StartWith("text: ");
        AppNoticeService.FindProblem(Valid with { IdempotencyKey = "has space" }).Should().Contain("idempotencyKey");
        AppNoticeService.FindProblem(Valid with { IdempotencyKey = new string('k', AppNoticeConstants.MaxIdempotencyKeyLength + 1) }).Should().Contain("idempotencyKey");
        AppNoticeService.FindProblem(Valid with { Culture = new string('c', 17) }).Should().Contain("culture");
        AppNoticeService.FindProblem(Valid with { Buttons = [.. Enumerable.Repeat(Valid.Buttons[0], AppNoticeConstants.MaxButtons + 1)] }).Should().Contain("at most");
        AppNoticeService.FindProblem(Valid with { Buttons = [new AppNoticeButton { Text = "Open", Url = "tg://resolve?domain=x" }] }).Should().Contain("https");
        AppNoticeService.FindProblem(Valid with { Buttons = [new AppNoticeButton { Text = "", Url = "https://a.test/" }] }).Should().Contain("text");
    }
}

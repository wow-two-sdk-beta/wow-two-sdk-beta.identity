using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests;

/// <summary>
/// <c>admin create</c>'s arguments and result line without a database: the options it reads, a usage error for anything
/// else, and exit <c>2</c> with one <c>admin-result</c> line when the arguments or the host's slices make no run.
/// </summary>
public sealed class AdminCommandTests
{
    [Fact]
    public void Parse_ShouldReadEveryOption_InBothForms()
    {
        var problem = AdminCommand.Parse(
            [
                "create", "--slug", "ocharo", "--name=Ocharo Group", "--organization-id", "0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5b",
                "--display-name", "Lola Karimova", "--phone", "+998901234567", "--email", "lola@example.test", "--user-id=0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c",
            ],
            out var request);

        problem.Should().BeNull();
        request.Should().Be(new FirstOwnerRequest
        {
            Slug = "ocharo",
            OrganizationName = "Ocharo Group",
            OrganizationId = Guid.Parse("0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5b"),
            DisplayName = "Lola Karimova",
            PhoneNumber = "+998901234567",
            Email = "lola@example.test",
            UserId = Guid.Parse("0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c"),
        });
    }

    [Theory]
    [InlineData(new string[0], "missing verb")]
    [InlineData(new[] { "list" }, "unknown verb 'list'")]
    [InlineData(new[] { "create", "--slug" }, "--slug needs a value")]
    [InlineData(new[] { "create", "--slug", "a", "--slug", "b" }, "--slug is given twice")]
    [InlineData(new[] { "create", "--slug", "a", "--name", "A", "--display-name", "L", "--password", "secret" }, "unknown option '--password'")]
    [InlineData(new[] { "create", "--name", "A", "--display-name", "L" }, "--slug is required")]
    [InlineData(new[] { "create", "--slug", "a", "--name", "A", "--display-name", "L", "--organization-id", "42" }, "--organization-id and --user-id take a uuid")]
    public void Parse_ShouldNameTheProblem_OfArgumentsThatMakeNoRequest(string[] arguments, string expected)
    {
        AdminCommand.Parse(arguments, out var request).Should().Be(expected);
        request.Should().BeNull();
    }

    [Fact]
    public async Task Run_ShouldExit2_WithTheUsageOnTheResultLine_ForAUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        await using var services = new ServiceCollection().BuildServiceProvider();

        var exit = await AdminCommand.RunAsync(services, ["create"], output, error);

        exit.Should().Be(2);
        var result = AdminCreateResult.FromOutput(output.ToString())!;
        result.Status.Should().Be("failed");
        result.Error.Should().Contain("--slug is required").And.Contain(AdminCommand.Usage);
        output.ToString().Trim().Split('\n').Should().ContainSingle().Which.Should().StartWith(AdminCreateResult.ResultLinePrefix);
        error.ToString().Should().StartWith("✗ admin create: failed (exit 2)");
    }

    [Fact]
    public async Task Run_ShouldExit2_WhenTheHostHasNoOrganizationsSlice()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        await using var services = new ServiceCollection().BuildServiceProvider();

        var exit = await AdminCommand.RunAsync(services, ["create", "--slug", "a", "--name", "A", "--display-name", "L", "--phone", "+998901234567"], output, error);

        exit.Should().Be(2);
        AdminCreateResult.FromOutput(output.ToString())!.Error.Should().Contain("AddOrganizations");
    }

    [Fact]
    public void ResultLine_ShouldLeaveAbsentFieldsOut_AndReadBack()
    {
        var result = new AdminCreateResult { Exit = 0, Status = "present", UserId = Guid.Parse("0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c"), DurationMs = 7 };

        result.ToResultLine().Should().Be("""admin-result {"verb":"create","exit":0,"status":"present","userId":"0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c","isOrganizationCreated":false,"isOwnerCreated":false,"durationMs":7}""");
        AdminCreateResult.FromOutput("noise\n" + result.ToResultLine() + "\n").Should().Be(result);
    }
}

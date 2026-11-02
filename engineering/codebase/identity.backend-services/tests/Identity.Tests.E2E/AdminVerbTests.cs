using AwesomeAssertions;
using Identity.Api.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace Identity.Tests.E2E;

/// <summary>
/// The image's <c>admin create</c> verb (<c>dotnet Identity.Api.dll admin create …</c>) over the services and verbs its
/// <c>Program</c> builds, on a fresh migrated database: exit <c>0</c> created, then <c>0</c> present on a rerun, <c>1</c>
/// refused on a conflict, <c>2</c> failed on a usage error or an invalid value, each ending in one <c>admin-result</c> line
/// on stdout and nothing else there. The host never serves.
/// </summary>
public sealed class AdminVerbTests : IAsyncLifetime
{
    private const string OrganizationId = "0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5b";
    private const string UserId = "0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c";

    private static readonly string[] Create =
    [
        "admin", "create", "--slug", "ocharo", "--name", "Ocharo", "--organization-id", OrganizationId,
        "--display-name", "Lola Karimova", "--phone", "+998901234567", "--email=lola@example.test", "--user-id", UserId,
    ];

    private string _connectionString = "";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        (await RunAsync("migrate", "apply")).Exit.Should().Be(0);
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await PostgresTestServer.DropDatabaseAsync(_connectionString);

    [Fact]
    public async Task Create_ShouldCreateBoth_ThenAnswerPresent_OnARerun()
    {
        var created = await RunAsync(Create);

        created.Exit.Should().Be(0, created.Error);
        created.Output.Trim().Split('\n').Should().ContainSingle("stdout carries the result line only");
        created.Result.Should().BeEquivalentTo(new
        {
            Verb = "create",
            Exit = 0,
            Status = "created",
            OrganizationId = Guid.Parse(OrganizationId),
            UserId = Guid.Parse(UserId),
            IsOrganizationCreated = true,
            IsOwnerCreated = true,
            Error = (string?)null,
        }, options => options.ExcludingMissingMembers());
        created.Error.Should().Contain("admin create: created");

        var present = await RunAsync(Create);

        present.Exit.Should().Be(0, present.Error);
        present.Result.Status.Should().Be("present");
        present.Result.IsOrganizationCreated.Should().BeFalse();
        present.Result.IsOwnerCreated.Should().BeFalse();
        present.Result.UserId.Should().Be(Guid.Parse(UserId));
    }

    [Fact]
    public async Task Create_ShouldExit1_WhenTheHandleBelongsToAnotherOrganization()
    {
        (await RunAsync(Create)).Exit.Should().Be(0);
        var other = Create.Select(argument => argument == OrganizationId ? Guid.CreateVersion7().ToString() : argument).ToArray();

        var refused = await RunAsync(other);

        refused.Exit.Should().Be(1, refused.Error);
        refused.Result.Status.Should().Be("refused");
        refused.Result.Error.Should().Contain("belongs to organization " + OrganizationId);
    }

    [Fact]
    public async Task Create_ShouldGiveTheOwnerTheInstanceAdminRole_OnARerunWithRole()
    {
        (await RunAsync(Create)).Exit.Should().Be(0);

        var granted = await RunAsync([.. Create, "--role", "admin"]);

        granted.Exit.Should().Be(0, granted.Error);
        granted.Result.Should().BeEquivalentTo(new { Status = "created", InstanceRole = "admin", IsInstanceRoleGranted = true, IsOwnerCreated = false }, options => options.ExcludingMissingMembers());
        granted.Error.Should().Contain("role         admin: granted");
        (await RunAsync([.. Create, "--role", "admin"])).Result.Status.Should().Be("present");
        (await RunAsync([.. Create, "--role", "owner"])).Exit.Should().Be(2, "owner is an organization role, not an instance role");
    }

    [Theory]
    [InlineData(new[] { "admin" }, "missing verb")]
    [InlineData(new[] { "admin", "delete" }, "unknown verb")]
    [InlineData(new[] { "admin", "invite", "--organization", "ocharo", "--role", "owner" }, "unknown verb 'invite'")]
    [InlineData(new[] { "admin", "create", "--slug", "ocharo", "--name", "Ocharo" }, "--display-name is required")]
    [InlineData(new[] { "admin", "create", "--slug", "ocharo", "--name", "Ocharo", "--display-name", "Lola", "--password", "x" }, "unknown option '--password'")]
    [InlineData(new[] { "admin", "create", "--slug", "ocharo", "--name", "Ocharo", "--display-name", "Lola" }, "a phone, an email or both")]
    [InlineData(new[] { "admin", "create", "--slug", "ocharo", "--name", "Ocharo", "--display-name", "Lola", "--phone", "+998901234567", "--user-id", "nope" }, "take a uuid")]
    public async Task Create_ShouldExit2_OnAUsageErrorOrAnInvalidValue(string[] arguments, string expected)
    {
        var failed = await RunAsync(arguments);

        failed.Exit.Should().Be(2, failed.Error);
        failed.Result.Status.Should().Be("failed");
        failed.Result.Error.Should().Contain(expected);
    }

    private async Task<Run> RunAsync(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Program.cs's own path to the verbs: the configured builder, built, never started, with the service's verbs.
        var exit = await ServiceHost.RunAsync(
            arguments,
            builder =>
            {
                builder.Environment.EnvironmentName = Environments.Development;
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseSettings:ConnectionString"] = _connectionString,
                    ["Identity:Server:Keys:IsEphemeral"] = "true",
                    ["Identity:Server:Issuer"] = "https://id.example.test/",
                    ["Logging:LogLevel:Default"] = "Warning",
                });
                builder.Configure();
            },
            _ => Task.CompletedTask,
            verbs => verbs.AddIdentityVerbs(),
            output,
            error,
            CancellationToken.None);

        var result = arguments is ["admin", ..]
            ? AdminCreateResult.FromOutput(output.ToString()) ?? throw new InvalidOperationException($"No admin-result line: {output}")
            : null;
        return new Run(exit, result!, output.ToString(), error.ToString());
    }

    private sealed record Run(int Exit, AdminCreateResult Result, string Output, string Error);
}

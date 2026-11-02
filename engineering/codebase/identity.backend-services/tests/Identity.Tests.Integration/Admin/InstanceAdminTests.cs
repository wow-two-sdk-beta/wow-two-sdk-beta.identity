using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Admin;

/// <summary>
/// <see cref="InstanceAdminService"/> over PostgreSQL, the logic behind <c>admin create</c>: the person created with a
/// preset id and given the instance role <c>admin</c>, present on a rerun, finished after a run that stopped halfway,
/// refused for a second admin and on every identifier conflict, and invalid before any write.
/// </summary>
public sealed class InstanceAdminTests
{
    private const string Phone = "+998901234567";
    private const string Email = "lola@example.test";
    private static readonly Guid UserId = Guid.Parse("0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c");

    private static InstanceAdminRequest Request => new()
    {
        DisplayName = "Lola Karimova",
        PhoneNumber = "+998 90 123 45 67",
        Email = Email,
        UserId = UserId,
    };

    [Fact]
    public async Task Create_ShouldMakeThePersonAnAdmin_WithThePresetId_ThenAnswerPresent()
    {
        await using var host = CreateHost();

        var created = await CreateAsync(host, Request);

        created.Should().BeEquivalentTo(new InstanceAdminResult
        {
            Status = InstanceAdminStatus.Created,
            UserId = UserId,
            IsUserCreated = true,
            IsRoleGranted = true,
        });
        var admin = await host.ReloadAsync(UserId);
        admin.Should().BeEquivalentTo(new
        {
            UserName = Email,
            DisplayName = "Lola Karimova",
            Email,
            IsEmailConfirmed = true,
            PhoneNumber = Phone,
            IsPhoneNumberConfirmed = true,
            PasswordHash = (string?)null,
        }, options => options.ExcludingMissingMembers());
        (await RolesOfAsync(host, UserId)).Should().Equal(InstanceRoleConstants.Admin);
        await using (var scope = host.Scope())
            (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Organizations.AnyAsync()).Should().BeFalse("organizations are made in the console");

        var again = await CreateAsync(host, Request);
        again.Should().BeEquivalentTo(new InstanceAdminResult { Status = InstanceAdminStatus.Present, UserId = UserId });
        (await CreateAsync(host, Request with { UserId = null, Email = null })).Status
            .Should().Be(InstanceAdminStatus.Present, "the confirmed phone alone finds the admin");
    }

    [Fact]
    public async Task Create_ShouldAssignTheId_AndTakeThePhoneAsTheUserName_ForAPhoneOnlyAdmin()
    {
        await using var host = CreateHost();

        var created = await CreateAsync(host, Request with { UserId = null, Email = null });

        created.Status.Should().Be(InstanceAdminStatus.Created);
        var admin = await host.ReloadAsync(created.UserId!.Value);
        admin.UserName.Should().Be(Phone);
        admin.Email.Should().BeNull();
    }

    [Fact]
    public async Task Create_ShouldGiveAnExistingPersonTheRole_WhenOnlyThePersonExists()
    {
        await using var host = CreateHost();
        await using (var scope = host.Scope())
        {
            var person = new IdentityUser { Id = UserId, UserName = Email, Email = Email, PhoneNumber = Phone, IsPhoneNumberConfirmed = true };
            (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(person)).IsSuccess.Should().BeTrue();
        }

        var finished = await CreateAsync(host, Request);

        finished.Should().BeEquivalentTo(new InstanceAdminResult { Status = InstanceAdminStatus.Created, UserId = UserId, IsRoleGranted = true });
        (await RolesOfAsync(host, UserId)).Should().Equal(InstanceRoleConstants.Admin);
    }

    [Fact]
    public async Task Create_ShouldRefuseASecondAdmin_BeforeAnyWrite()
    {
        await using var host = CreateHost();
        (await CreateAsync(host, Request)).Status.Should().Be(InstanceAdminStatus.Created);
        var existing = await host.CreateUserAsync("ziyoda", "ziyoda@example.test");
        await ConfirmEmailAsync(host, existing.Id);

        var stranger = await CreateAsync(host, new InstanceAdminRequest { DisplayName = "Aziz", PhoneNumber = "+998907654321" });
        stranger.Status.Should().Be(InstanceAdminStatus.Refused);
        stranger.Error.Should().Contain($"already {UserId}").And.Contain("once");
        var present = await CreateAsync(host, new InstanceAdminRequest { DisplayName = "Ziyoda", Email = "ziyoda@example.test" });
        present.Should().BeEquivalentTo(new { Status = InstanceAdminStatus.Refused, UserId = (Guid?)existing.Id }, options => options.ExcludingMissingMembers());

        await using var scope = host.Scope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.CountAsync()).Should().Be(2, "a refused run creates nobody");
        (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GetHoldersAsync("admin")).Should().Equal(UserId);
    }

    [Fact]
    public async Task Create_ShouldAnswerPresent_ForTheAdmin_WhileAnotherPersonHoldsAdminToo()
    {
        await using var host = CreateHost();
        (await CreateAsync(host, Request)).Status.Should().Be(InstanceAdminStatus.Created);
        var other = await host.CreateUserAsync("ziyoda", "ziyoda@example.test");
        await using (var scope = host.Scope())
            (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(other, "admin")).IsGranted.Should().BeTrue();

        (await CreateAsync(host, Request)).Status.Should().Be(InstanceAdminStatus.Present, "a deploy's rerun naming the first admin stays green");
    }

    [Fact]
    public async Task Create_ShouldRefuse_WhenTheIdentifiersNameSomeoneElse()
    {
        await using var host = CreateHost();
        (await CreateAsync(host, Request)).Status.Should().Be(InstanceAdminStatus.Created);
        var stranger = await host.CreateUserAsync("ziyoda", "ziyoda@example.test");

        (await CreateAsync(host, Request with { UserId = Guid.CreateVersion7() })).Error.Should().Contain($"belongs to person {UserId}");
        (await CreateAsync(host, Request with { Email = "ziyoda@example.test" })).Error.Should().Contain("different people");
        (await CreateAsync(host, Request with { PhoneNumber = "+998907654321" })).Error.Should().Contain("another phone");
        (await CreateAsync(host, Request with { UserId = null, PhoneNumber = null, Email = "ziyoda@example.test" })).Should()
            .BeEquivalentTo(new { Status = InstanceAdminStatus.Refused, UserId = (Guid?)stranger.Id }, options => options.ExcludingMissingMembers());
        (await RolesOfAsync(host, stranger.Id)).Should().BeEmpty("a refused run gives nobody a role");
    }

    [Fact]
    public async Task Create_ShouldRefuseToAdoptASignUpWithTheAdminsUnconfirmedEmail_UnlessNamedById()
    {
        await using var host = CreateHost();
        var squatter = await host.CreateUserAsync("squatter", Email);

        var refused = await CreateAsync(host, Request with { UserId = null, PhoneNumber = null });

        refused.Status.Should().Be(InstanceAdminStatus.Refused, "anyone can sign up with an address they do not hold");
        refused.Error.Should().Contain("unconfirmed").And.Contain("--user-id");
        (await RolesOfAsync(host, squatter.Id)).Should().BeEmpty("a refused run gives nobody a role");
        (await CreateAsync(host, Request with { UserId = squatter.Id, PhoneNumber = null })).Status
            .Should().Be(InstanceAdminStatus.Created, "the operator named the person by id");
    }

    [Theory]
    [InlineData(" ", Phone, null, "display name")]
    [InlineData("Lola", null, null, "a phone, an email or both")]
    [InlineData("Lola", "12345", null, "international form")]
    [InlineData("Lola", "+77011234567", null, "allowed countries")]
    [InlineData("Lola", null, "not an email", "not an email address")]
    public async Task Create_ShouldAnswerInvalid_BeforeAnyWrite(string displayName, string? phone, string? email, string expected)
    {
        await using var host = CreateHost();

        var result = await CreateAsync(host, new InstanceAdminRequest { DisplayName = displayName, PhoneNumber = phone, Email = email });

        result.Status.Should().Be(InstanceAdminStatus.Invalid);
        result.Error.Should().Contain(expected);
        await using var scope = host.Scope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Create_ShouldAnswerInvalid_ForTheEmptyUuid()
    {
        await using var host = CreateHost();

        (await CreateAsync(host, Request with { UserId = Guid.Empty })).Error.Should().Contain("empty uuid");
    }

    private static async Task ConfirmEmailAsync(AccountsHost host, Guid userId)
    {
        await using var scope = host.Scope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
        var user = (await accounts.FindByIdAsync(userId))!;
        user.IsEmailConfirmed = true;
        await accounts.UpdateAsync(user);
    }

    private static async Task<IReadOnlyList<string>> RolesOfAsync(AccountsHost host, Guid userId)
    {
        await using var scope = host.Scope();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        return await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GetAsync(person);
    }

    private static AccountsHost CreateHost() => AccountsHost.Create(identity => identity.AddRoles().AddPhoneNumbers(phones => phones.AllowedCountries = ["UZ"]));

    private static async Task<InstanceAdminResult> CreateAsync(AccountsHost host, InstanceAdminRequest request)
    {
        await using var scope = host.Scope();
        return await ActivatorUtilities.CreateInstance<InstanceAdminService>(scope.ServiceProvider).CreateAsync(request);
    }
}

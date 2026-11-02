using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Admin;

/// <summary>
/// <see cref="FirstOwnerService"/> over PostgreSQL, the logic behind <c>admin create</c>: the organization and its owner
/// created with preset ids, present on a rerun, finished after a run that stopped halfway, refused on every conflict and
/// invalid before any write.
/// </summary>
public sealed class FirstOwnerTests
{
    private const string Phone = "+998901234567";
    private const string Email = "lola@example.test";
    private static readonly Guid OrganizationId = Guid.Parse("0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid UserId = Guid.Parse("0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5c");

    private static FirstOwnerRequest Request => new()
    {
        Slug = "ocharo",
        OrganizationName = "Ocharo",
        OrganizationId = OrganizationId,
        DisplayName = "Lola Karimova",
        PhoneNumber = "+998 90 123 45 67",
        Email = Email,
        UserId = UserId,
    };

    [Fact]
    public async Task Create_ShouldMakeTheOrganizationAndItsOwner_WithThePresetIds_ThenAnswerPresent()
    {
        await using var host = CreateHost();

        var created = await CreateAsync(host, Request);

        created.Should().BeEquivalentTo(new FirstOwnerResult
        {
            Status = FirstOwnerStatus.Created,
            OrganizationId = OrganizationId,
            UserId = UserId,
            IsOrganizationCreated = true,
            IsOwnerCreated = true,
        });
        var owner = await host.ReloadAsync(UserId);
        owner.Should().BeEquivalentTo(new
        {
            UserName = Email,
            DisplayName = "Lola Karimova",
            Email,
            IsEmailConfirmed = true,
            PhoneNumber = Phone,
            IsPhoneNumberConfirmed = true,
            PasswordHash = (string?)null,
        }, options => options.ExcludingMissingMembers());
        await using (var scope = host.Scope())
        {
            var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
            var ocharo = (await organizations.FindBySlugAsync("ocharo"))!;
            ocharo.Id.Should().Be(OrganizationId);
            (await organizations.GetRoleAsync(ocharo, owner)).Should().Be("owner");
        }

        var again = await CreateAsync(host, Request);
        again.Should().BeEquivalentTo(new FirstOwnerResult { Status = FirstOwnerStatus.Present, OrganizationId = OrganizationId, UserId = UserId });
        (await CreateAsync(host, Request with { OrganizationId = null, UserId = null, Email = null })).Status
            .Should().Be(FirstOwnerStatus.Present, "the handle and the phone alone find both");
    }

    [Fact]
    public async Task Create_ShouldAssignIds_AndTakeThePhoneAsTheUserName_ForAPhoneOnlyOwner()
    {
        await using var host = CreateHost();

        var created = await CreateAsync(host, Request with { OrganizationId = null, UserId = null, Email = null });

        created.Status.Should().Be(FirstOwnerStatus.Created);
        created.OrganizationId.Should().NotBeEmpty();
        var owner = await host.ReloadAsync(created.UserId!.Value);
        owner.UserName.Should().Be(Phone);
        owner.Email.Should().BeNull();
    }

    [Fact]
    public async Task Create_ShouldFinishTheOrganization_WhenOnlyTheOwnerExists()
    {
        await using var host = CreateHost();
        await using (var scope = host.Scope())
        {
            var person = new IdentityUser { Id = UserId, UserName = Email, Email = Email, PhoneNumber = Phone, IsPhoneNumberConfirmed = true };
            (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(person)).IsSuccess.Should().BeTrue();
        }

        var finished = await CreateAsync(host, Request);

        finished.Status.Should().Be(FirstOwnerStatus.Created);
        finished.IsOrganizationCreated.Should().BeTrue();
        finished.IsOwnerCreated.Should().BeFalse("the person existed with the same id, email and phone");
    }

    [Fact]
    public async Task Create_ShouldRefuse_WhenTheHandleOrAnIdBelongsToAnotherOrganization()
    {
        await using var host = CreateHost();
        (await CreateAsync(host, Request)).Status.Should().Be(FirstOwnerStatus.Created);

        var otherId = await CreateAsync(host, Request with { OrganizationId = Guid.CreateVersion7() });
        otherId.Status.Should().Be(FirstOwnerStatus.Refused);
        otherId.Error.Should().Contain("belongs to organization");

        var otherSlug = await CreateAsync(host, Request with { Slug = "desk" });
        otherSlug.Status.Should().Be(FirstOwnerStatus.Refused);
        otherSlug.Error.Should().Contain("exists with the handle 'ocharo'");
    }

    [Fact]
    public async Task Create_ShouldRefuse_WhenThePersonExistsWithOtherIdsOrContacts_OrIsNoOwner()
    {
        await using var host = CreateHost();
        (await CreateAsync(host, Request)).Status.Should().Be(FirstOwnerStatus.Created);
        var stranger = await host.CreateUserAsync("ziyoda", "ziyoda@example.test");

        (await CreateAsync(host, Request with { UserId = Guid.CreateVersion7() })).Error.Should().Contain($"belongs to person {UserId}");
        (await CreateAsync(host, Request with { Email = "ziyoda@example.test" })).Error.Should().Contain("different people");
        (await CreateAsync(host, Request with { PhoneNumber = "+998907654321" })).Error.Should().Contain("another phone");
        var unconfirmed = await CreateAsync(host, Request with { UserId = null, PhoneNumber = null, Email = "ziyoda@example.test" });
        unconfirmed.Status.Should().Be(FirstOwnerStatus.Refused);
        unconfirmed.Error.Should().Contain($"Person {stranger.Id} holds 'ziyoda@example.test' unconfirmed");
        await ConfirmEmailAsync(host, stranger.Id);
        var notOwner = await CreateAsync(host, Request with { UserId = null, PhoneNumber = null, Email = "ziyoda@example.test" });
        notOwner.Status.Should().Be(FirstOwnerStatus.Refused);
        notOwner.Error.Should().Contain($"Person {stranger.Id} is not an owner");
        var nobody = await CreateAsync(host, Request with { UserId = null, PhoneNumber = null, Email = "new@example.test" });
        nobody.Error.Should().Contain("is not among its owners");

        await using var scope = host.Scope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        (await organizations.ListMembersAsync((await organizations.FindBySlugAsync("ocharo"))!)).Should().ContainSingle("a refused run changes nothing");
    }

    [Fact]
    public async Task Create_ShouldRefuseToAdoptASignUpWithTheOwnersUnconfirmedEmail_UnlessNamedById()
    {
        await using var host = CreateHost();
        var squatter = await host.CreateUserAsync("squatter", Email);

        var refused = await CreateAsync(host, Request with { UserId = null, PhoneNumber = null });

        refused.Status.Should().Be(FirstOwnerStatus.Refused, "anyone can sign up with an address they do not hold");
        refused.Error.Should().Contain("unconfirmed").And.Contain("--user-id");
        await using (var scope = host.Scope())
            (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Organizations.AnyAsync()).Should().BeFalse("a refused run creates nothing");
        (await CreateAsync(host, Request with { UserId = squatter.Id, PhoneNumber = null })).Status.Should().Be(FirstOwnerStatus.Created, "the operator named the person by id");
    }

    [Theory]
    [InlineData("", "Ocharo", "Lola", Phone, null, "not a valid handle")]
    [InlineData("ocharo", " ", "Lola", Phone, null, "needs a name")]
    [InlineData("ocharo", "Ocharo", " ", Phone, null, "display name")]
    [InlineData("ocharo", "Ocharo", "Lola", null, null, "a phone, an email or both")]
    [InlineData("ocharo", "Ocharo", "Lola", "12345", null, "international form")]
    [InlineData("ocharo", "Ocharo", "Lola", "+77011234567", null, "allowed countries")]
    [InlineData("ocharo", "Ocharo", "Lola", null, "not an email", "not an email address")]
    public async Task Create_ShouldAnswerInvalid_BeforeAnyWrite(string slug, string name, string displayName, string? phone, string? email, string expected)
    {
        await using var host = CreateHost();

        var result = await CreateAsync(host, new FirstOwnerRequest { Slug = slug, OrganizationName = name, DisplayName = displayName, PhoneNumber = phone, Email = email });

        result.Status.Should().Be(FirstOwnerStatus.Invalid);
        result.Error.Should().Contain(expected);
        await using var scope = host.Scope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AnyAsync()).Should().BeFalse();
    }

    private static async Task ConfirmEmailAsync(AccountsHost host, Guid userId)
    {
        await using var scope = host.Scope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
        var user = (await accounts.FindByIdAsync(userId))!;
        user.IsEmailConfirmed = true;
        await accounts.UpdateAsync(user);
    }

    private static AccountsHost CreateHost() => AccountsHost.Create(identity => identity
        .AddOrganizations()
        .AddPhoneNumbers(phones => phones.AllowedCountries = ["UZ"]));

    private static async Task<FirstOwnerResult> CreateAsync(AccountsHost host, FirstOwnerRequest request)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<FirstOwnerService>().CreateAsync(request);
    }
}

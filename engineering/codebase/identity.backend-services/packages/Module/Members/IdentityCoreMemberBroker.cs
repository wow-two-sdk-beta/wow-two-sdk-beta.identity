using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Members;

/// <summary>
/// Integrates organization members in process (Form 1): the organization's memberships, earliest joined first, each with the
/// person's <c>sub</c>, role and whether their confirmed phone is linked to the instance's Telegram bot. The service's
/// route answers through it for each scoped client.
/// </summary>
/// <remarks>
///   - an organization the instance lacks fails <c>OrganizationNotFound</c>; a role outside the catalog fails
///     <c>UnknownOrganizationRole</c>
///   - the Telegram flag reads the bot's links when the bot is registered (<c>AddTelegramLinks</c>), else false
/// </remarks>
/// <param name="organizations">The organizations and their memberships.</param>
/// <param name="accounts">The members' accounts.</param>
/// <param name="services">The scope, for the bot's links when they are registered.</param>
public sealed class IdentityCoreMemberBroker(OrganizationService organizations, UserAccountService accounts, IServiceProvider services) : IMemberBroker
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MemberModel>>> ListAsync(Guid organizationId, string? role = null, CancellationToken cancellationToken = default)
    {
        string? only = null;
        if (!string.IsNullOrWhiteSpace(role) && (only = OrganizationRoleConstants.Find(role)) is null)
        {
            return Result<IReadOnlyList<MemberModel>>.Fail(InvitationErrors.Coded(
                AppErrorType.Validation, $"'{role.Trim()}' is not an organization role.", IdentityErrorCodeConstants.UnknownOrganizationRole));
        }

        if (await organizations.FindByIdAsync(organizationId, cancellationToken) is not { } organization)
            return Result<IReadOnlyList<MemberModel>>.Fail(InvitationErrors.Coded(AppErrorType.NotFound, "No such organization.", IdentityErrorCodeConstants.OrganizationNotFound));

        var found = new List<(IdentityMembership Membership, string? Phone)>();
        // Earliest joined first; people who joined at the same instant keep one order across calls.
        foreach (var membership in (await organizations.ListMembersAsync(organization, cancellationToken)).OrderBy(m => m.CreatedAt).ThenBy(m => m.UserId))
        {
            if (only is not null && !string.Equals(membership.Role, only, StringComparison.OrdinalIgnoreCase))
                continue;
            if (await accounts.FindByIdAsync(membership.UserId, cancellationToken) is { } user)
                found.Add((membership, user.IsPhoneNumberConfirmed ? PhoneNumberPolicy.Normalize(user.PhoneNumber) : null));
        }

        var linked = await LinkedNumbersAsync([.. found.Select(member => member.Phone).OfType<string>().Distinct(StringComparer.Ordinal)], cancellationToken);
        IReadOnlyList<MemberModel> members =
        [
            .. found.Select(member => new MemberModel
            {
                Subject = member.Membership.UserId.ToString("D"),
                Role = OrganizationRoleConstants.Find(member.Membership.Role) ?? member.Membership.Role,
                HasTelegramLink = member.Phone is not null && linked.Contains(member.Phone),
            }),
        ];
        return Result<IReadOnlyList<MemberModel>>.Ok(members);
    }

    /// <summary>The numbers among <paramref name="phones"/> linked to the instance's bot; none without a bot.</summary>
    private async Task<IReadOnlySet<string>> LinkedNumbersAsync(IReadOnlyCollection<string> phones, CancellationToken cancellationToken)
    {
        var links = services.GetService<ITelegramLinkRepository>();
        var bot = services.GetService<IOptionsMonitor<TelegramBotOptions>>()?.CurrentValue.NormalizedBotUserName;
        return links is null || string.IsNullOrEmpty(bot) || phones.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : await links.FindLinkedNumbersAsync(bot, phones, cancellationToken);
    }
}

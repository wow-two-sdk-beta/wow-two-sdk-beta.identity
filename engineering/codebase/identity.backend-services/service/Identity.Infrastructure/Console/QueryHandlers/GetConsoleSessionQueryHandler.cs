using Identity.Application.Console.Models;
using Identity.Application.Console.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Infrastructure.Console.QueryHandlers;

/// <inheritdoc />
public sealed class GetConsoleSessionQueryHandler(UserAccountService accounts, InstanceRoleService instanceRoles, OrganizationService organizations)
    : IQueryHandler<GetConsoleSessionQuery, AppResult<ConsoleSession>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ConsoleSession>> HandleAsync(GetConsoleSessionQuery query, CancellationToken cancellationToken)
    {
        if (query.CallerId is not { } id || await accounts.FindByIdAsync(id, cancellationToken) is not { } user)
            return AppResult<ConsoleSession>.Fail(AppErrorFactory.Unauthorized());

        var roles = await instanceRoles.GetAsync(user, cancellationToken);
        IReadOnlyList<ConsoleOrganization> managed = [.. (await organizations.ListForUserAsync(user, cancellationToken))
            .Where(membership => membership.Role is OrganizationRoleConstants.Owner or OrganizationRoleConstants.Admin)
            .OrderBy(membership => membership.JoinedAt)
            .Select(membership => new ConsoleOrganization(membership.OrganizationId, membership.Name, membership.Slug, membership.Role))];

        if (roles.Count == 0 && managed.Count == 0)
            return AppResult<ConsoleSession>.Fail(AppErrorFactory.Forbidden());

        var name = new[] { user.DisplayName, user.PhoneNumber, user.UserName }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? user.Id.ToString("D");
        return AppResult<ConsoleSession>.Ok(new ConsoleSession(user.Id, name.Trim(), roles, managed));
    }
}

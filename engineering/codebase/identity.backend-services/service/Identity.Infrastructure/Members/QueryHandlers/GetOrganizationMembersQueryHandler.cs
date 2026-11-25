using Identity.Application.Members.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Members;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Infrastructure.Members.QueryHandlers;

/// <inheritdoc />
public sealed class GetOrganizationMembersQueryHandler(IMemberBroker members)
    : IQueryHandler<GetOrganizationMembersQuery, AppResult<IReadOnlyList<MemberModel>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<MemberModel>>> HandleAsync(GetOrganizationMembersQuery query, CancellationToken cancellationToken)
    {
        if (query.BoundOrganizationId is { Length: > 0 } bound
            && !(Guid.TryParse(bound, out var boundId) && boundId == query.OrganizationId))
        {
            return AppResult<IReadOnlyList<MemberModel>>.Fail(AppError.Of(AppErrorType.NotFound, "No such organization.",
                new Dictionary<string, object?> { [MemberConstants.CodeMetadataKey] = IdentityErrorCodeConstants.OrganizationNotFound }));
        }

        var result = await members.ListAsync(query.OrganizationId, query.Role, cancellationToken);

        return result.Match(value => AppResult<IReadOnlyList<MemberModel>>.Ok(value), error => AppResult<IReadOnlyList<MemberModel>>.Fail(error));
    }
}

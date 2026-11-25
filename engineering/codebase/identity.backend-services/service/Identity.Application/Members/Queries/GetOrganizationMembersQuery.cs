using WoW.Two.Sdk.Beta.Be.Identity.Members;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Application.Members.Queries;

/// <summary>Reads organization membership for an authenticated service client.</summary>
public sealed record GetOrganizationMembersQuery(Guid OrganizationId, string? Role, string? BoundOrganizationId)
    : IQuery<AppResult<IReadOnlyList<MemberModel>>>;

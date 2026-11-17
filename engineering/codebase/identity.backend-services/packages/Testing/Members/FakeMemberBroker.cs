using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Members;

/// <summary>
/// Stands in for Identity's organization members with no instance: the test adds organizations and members, each with a
/// catalog role and a Telegram flag, and the contract's rules hold — earliest added first, a catalog role filter in any case,
/// <c>OrganizationNotFound</c> and <c>UnknownOrganizationRole</c> in the error's <c>identityCode</c>.
/// </summary>
public sealed class FakeMemberBroker : IMemberBroker
{
    private static readonly string[] Roles = ["owner", "admin", "editor", "viewer", "billing"];

    private readonly object _gate = new();
    private readonly Dictionary<Guid, List<MemberModel>> _organizations = [];
    private readonly Queue<AppError> _failures = new();
    private readonly List<(Guid OrganizationId, string? Role)> _asked = [];

    /// <summary>The organizations listed so far, with the role each call asked for, in order.</summary>
    public IReadOnlyList<(Guid OrganizationId, string? Role)> Asked
    {
        get
        {
            lock (_gate)
                return [.. _asked];
        }
    }

    /// <summary>Adds an organization with no members yet; adding a member adds its organization too.</summary>
    /// <param name="organizationId">The organization's id.</param>
    public FakeMemberBroker AddOrganization(Guid organizationId)
    {
        lock (_gate)
            _organizations.TryAdd(organizationId, []);
        return this;
    }

    /// <summary>Adds <paramref name="sub"/> to the organization with <paramref name="role"/>, linked to the bot or not.</summary>
    /// <param name="organizationId">The organization's id.</param>
    /// <param name="sub">The person's <c>sub</c>.</param>
    /// <param name="role">A catalog role: <c>owner</c>, <c>admin</c>, <c>editor</c>, <c>viewer</c> or <c>billing</c>.</param>
    /// <param name="hasTelegramLink">Whether the person's confirmed phone is linked to the bot.</param>
    public FakeMemberBroker AddMember(Guid organizationId, string sub, string role = "viewer", bool hasTelegramLink = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        var catalog = Find(role) ?? throw new ArgumentException($"'{role}' is not a catalog role.", nameof(role));
        lock (_gate)
        {
            if (!_organizations.TryGetValue(organizationId, out var members))
                _organizations[organizationId] = members = [];
            members.RemoveAll(member => string.Equals(member.Subject, sub, StringComparison.OrdinalIgnoreCase));
            members.Add(new MemberModel { Subject = sub, Role = catalog, HasTelegramLink = hasTelegramLink });
        }

        return this;
    }

    /// <summary>Makes the next list fail with <paramref name="error"/>, such as an instance that cannot be reached.</summary>
    /// <param name="error">The failure.</param>
    public FakeMemberBroker FailNext(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_gate)
            _failures.Enqueue(error);
        return this;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<MemberModel>>> ListAsync(Guid organizationId, string? role = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _asked.Add((organizationId, role));
            if (_failures.TryDequeue(out var failure))
                return Task.FromResult(Result<IReadOnlyList<MemberModel>>.Fail(failure));

            string? only = null;
            if (!string.IsNullOrWhiteSpace(role) && (only = Find(role)) is null)
                return Task.FromResult(Fail(AppErrorType.Validation, $"'{role.Trim()}' is not an organization role.", "UnknownOrganizationRole"));
            if (!_organizations.TryGetValue(organizationId, out var members))
                return Task.FromResult(Fail(AppErrorType.NotFound, "No such organization.", "OrganizationNotFound"));

            IReadOnlyList<MemberModel> listed = [.. members.Where(member => only is null || member.Role == only)];
            return Task.FromResult(Result<IReadOnlyList<MemberModel>>.Ok(listed));
        }
    }

    private static string? Find(string? role)
        => string.IsNullOrWhiteSpace(role) ? null : Roles.FirstOrDefault(name => string.Equals(name, role.Trim(), StringComparison.OrdinalIgnoreCase));

    private static Result<IReadOnlyList<MemberModel>> Fail(AppErrorType type, string message, string code)
        => Result<IReadOnlyList<MemberModel>>.Fail(AppError.Of(type, message, new Dictionary<string, object?>(StringComparer.Ordinal) { [MemberConstants.CodeMetadataKey] = code }));
}

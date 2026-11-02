namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>
/// Provides the instance roles of <see cref="InstanceRoleConstants"/>: roles over the instance itself, held in
/// <c>identity_roles</c> beside a person's organization roles. They open the console (a cookie session carries them as role
/// claims) and never enter an app's tokens, whose claims the OIDC server writes from scratch.
/// </summary>
/// <remarks>
///   - the catalog's rows are seeded on first use: granting a role creates its row when missing
///   - granting is idempotent: a role already held answers success with <see cref="InstanceRoleGrant.IsGranted"/> false
///   - giving a role signs the person out everywhere (<see cref="RoleChangeSignOut"/>): they sign in again carrying it; a
///     role already held changes nothing
/// </remarks>
/// <param name="roles">Creates and finds role rows.</param>
/// <param name="memberships">Adds and reads a person's roles.</param>
public sealed class InstanceRoleService(RoleService roles, UserRoleService memberships)
{
    /// <summary>Gives <paramref name="user"/> the instance role <paramref name="role"/>, seeding its row when missing.</summary>
    /// <param name="user">The person.</param>
    /// <param name="role">One of <see cref="InstanceRoleConstants"/>, any case.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InstanceRoleGrant> GrantAsync(IdentityUser user, string? role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (InstanceRoleConstants.Find(role) is not { } name)
        {
            return new InstanceRoleGrant
            {
                Result = IdentityUserExtensions.Failure(
                    IdentityErrorCodeConstants.UnknownInstanceRole,
                    $"Role '{role}' is not an instance role; use {string.Join(", ", InstanceRoleConstants.All)}.",
                    IdentityUserExtensions.Param("Role", role)),
            };
        }

        var seeded = await SeedAsync(name, cancellationToken);
        if (!seeded.IsSuccess)
            return new InstanceRoleGrant { Result = seeded, Role = name };
        if (await memberships.IsInRoleAsync(user, name, cancellationToken))
            return new InstanceRoleGrant { Result = IdentityResult.Success, Role = name };

        var added = await memberships.AddToRoleAsync(user, name, cancellationToken);
        return new InstanceRoleGrant { Result = added, Role = name, IsGranted = added.IsSuccess };
    }

    /// <summary>The instance roles <paramref name="user"/> holds, in catalog order.</summary>
    /// <param name="user">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<string>> GetAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var held = await memberships.GetRolesAsync(user, cancellationToken);
        return [.. InstanceRoleConstants.All.Where(name => held.Contains(name, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>The ids of the people holding the instance role <paramref name="role"/>; empty when nobody holds it or the role is outside the catalog.</summary>
    /// <param name="role">One of <see cref="InstanceRoleConstants"/>, any case.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<Guid>> GetHoldersAsync(string? role, CancellationToken cancellationToken = default)
        => InstanceRoleConstants.Find(role) is { } name ? await memberships.GetUserIdsInRoleAsync(name, cancellationToken) : [];

    /// <summary>Creates the catalog row of <paramref name="name"/> unless it exists.</summary>
    private async Task<IdentityResult> SeedAsync(string name, CancellationToken cancellationToken)
    {
        if (await roles.FindByNameAsync(name, cancellationToken) is not null)
            return IdentityResult.Success;

        var created = await roles.CreateAsync(new IdentityRole { Name = name }, cancellationToken);

        // A concurrent grant may have seeded it first.
        return created.IsSuccess || await roles.FindByNameAsync(name, cancellationToken) is not null ? IdentityResult.Success : created;
    }
}

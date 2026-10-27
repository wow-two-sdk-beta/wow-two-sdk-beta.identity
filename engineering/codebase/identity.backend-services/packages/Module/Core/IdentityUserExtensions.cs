namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Applies the stamp and lockout column rules shared by the identity slices.</summary>
internal static class IdentityUserExtensions
{
    /// <summary>Replaces the security stamp, voiding tokens, cookies and purpose tokens bound to the previous one.</summary>
    /// <param name="user">The user whose stamp rotates.</param>
    internal static void RotateSecurityStamp(this IdentityUser user)
        => user.SecurityStamp = Guid.NewGuid().ToString("N");

    /// <summary>Whether the user is inside an active lockout window at <paramref name="now"/>.</summary>
    /// <param name="user">The user to inspect.</param>
    /// <param name="now">The current instant.</param>
    internal static bool IsLockedOutAt(this IdentityUser user, DateTimeOffset now)
        => user.LockoutEnabled && user.LockoutEnd is { } end && end > now;

    /// <summary>Creates a failed result carrying one error.</summary>
    /// <param name="code">The stable error code.</param>
    /// <param name="description">The human-readable description.</param>
    /// <param name="parameters">The values the description was built from, by placeholder name.</param>
    internal static IdentityResult Failure(string code, string description, IReadOnlyDictionary<string, object>? parameters = null)
        => IdentityResult.Failed(new IdentityError { Code = code, Description = description, Params = parameters });

    /// <summary>One placeholder value for <see cref="Failure"/>.</summary>
    /// <param name="name">The placeholder name.</param>
    /// <param name="value">Its value; null becomes an empty string.</param>
    internal static IReadOnlyDictionary<string, object> Param(string name, object? value)
        => new Dictionary<string, object>(StringComparer.Ordinal) { [name] = value ?? string.Empty };
}

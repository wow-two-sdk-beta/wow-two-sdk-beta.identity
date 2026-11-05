using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using WoW.Two.Sdk.Beta.Be.Identity.CurrentUser;
using WoW.Two.Sdk.Beta.Be.Identity.Guest;
using WoW.Two.Sdk.Beta.Be.Identity.Guest.Serializers;

namespace WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;

/// <summary>Answers <see cref="ICurrentPrincipal"/> from the ambient request, reading its user once per request.</summary>
/// <remarks>Singleton and request-safe: it keeps nothing between requests. Outside a request it reports anonymous.</remarks>
public sealed class HttpCurrentPrincipal : ICurrentPrincipal
{
    private static readonly object ItemKey = new();

    private readonly IHttpContextAccessor _accessor;
    private readonly CurrentUserOptions _options;
    private readonly GuestCookieSerializer _serializer;

    /// <summary>Creates the principal over the request accessor and resolution options.</summary>
    /// <param name="accessor">Accessor for the ambient <see cref="HttpContext"/>.</param>
    /// <param name="options">Guest-cookie name and subject-claim type.</param>
    /// <param name="protection">Application-scoped Data Protection provider.</param>
    /// <param name="clock">Clock used to verify a guest cookie's expiry.</param>
    public HttpCurrentPrincipal(IHttpContextAccessor accessor, CurrentUserOptions options, IDataProtectionProvider protection, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(options);
        _accessor = accessor;
        _options = options;
        _serializer = new GuestCookieSerializer(protection, clock);
    }

    /// <inheritdoc />
    public string? Subject => Current.Subject;

    /// <inheritdoc />
    public PrincipalKind Kind => Current.Kind;

    /// <inheritdoc />
    public string? Issuer => Current.Issuer;

    /// <inheritdoc />
    public string? ClientId => Current.ClientId;

    /// <inheritdoc />
    public string? OrganizationId => Current.OrganizationId;

    /// <inheritdoc />
    public IReadOnlySet<string> Roles => Current.Roles;

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions => Current.Permissions;

    /// <inheritdoc />
    public IReadOnlySet<string> Scopes => Current.Scopes;

    /// <inheritdoc />
    public ActorInfo? Actor => Current.Actor;

    /// <inheritdoc />
    public string? Partition => Current.Partition;

    private NormalizedPrincipal Current
    {
        get
        {
            if (_accessor.HttpContext is not { } http)
                return NormalizedPrincipal.Anonymous;

            // Middleware may replace the user during a request, so the read holds only for the user it came from.
            if (http.Items.TryGetValue(ItemKey, out var read) && read is (ClaimsPrincipal user, NormalizedPrincipal cached) && ReferenceEquals(user, http.User))
                return cached;

            var current = ClaimsPrincipalNormalizer.Normalize(http.User, _options.SubjectClaimType);
            if (current.Kind == PrincipalKind.Anonymous && http.ReadGuest(_options.GuestCookieName, _serializer) is { } guest)
                current = new NormalizedPrincipal { Kind = PrincipalKind.Guest, Subject = guest.ToString("D") };

            http.Items[ItemKey] = (http.User, current);
            return current;
        }
    }
}

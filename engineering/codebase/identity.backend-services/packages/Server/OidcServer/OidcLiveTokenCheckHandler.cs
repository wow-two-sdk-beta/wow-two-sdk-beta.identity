using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Introspection;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Answers introspection with what stands now, not what the token says: <c>active: false</c> once the person's account is gone,
/// their session ended, or their membership or role in the token's organization differs. It takes the place of OpenIddict's
/// presenter-or-audience rule: a service's client id is not its resource URI, and any client granted introspection is a
/// first-party service asking about a token it was handed.
/// </summary>
/// <param name="services">The request's services.</param>
internal sealed class OidcLiveTokenCheckHandler(IServiceProvider services) : IOpenIddictServerHandler<ValidateIntrospectionRequestContext>
{
    /// <summary>The handler's registration, at the place of <see cref="ValidateAuthorizedParty"/>.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateIntrospectionRequestContext>()
            .UseScopedHandler<OidcLiveTokenCheckHandler>()
            .SetOrder(ValidateAuthorizedParty.Descriptor.Order)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    /// <inheritdoc />
    public async ValueTask HandleAsync(ValidateIntrospectionRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.GenericTokenPrincipal is not { } token || !Guid.TryParse(token.GetClaim(Claims.Subject), out var subject))
            return;

        var cancellationToken = context.CancellationToken;
        var person = await services.GetRequiredService<UserAccountService>().FindByIdAsync(subject, cancellationToken);
        if (person is null)
        {
            context.Reject(Errors.InvalidToken, "The token's person is gone.");
            return;
        }

        var (isLive, _) = await OidcServerEndpointRouteBuilderExtensions.SessionOfAsync(services, token, person, cancellationToken);
        if (!isLive)
        {
            context.Reject(Errors.InvalidToken, "The token's sign-in session has ended.");
            return;
        }

        if (!await IsRoleCurrentAsync(token, person, cancellationToken))
            context.Reject(Errors.InvalidToken, "The token's organization role has changed.");
    }

    /// <summary>Whether the token's organization and role still stand; a token naming no organization has nothing to differ.</summary>
    private async Task<bool> IsRoleCurrentAsync(ClaimsPrincipal token, IdentityUser person, CancellationToken cancellationToken)
    {
        if (token.GetClaim(OrganizationClaimTypeConstants.Id) is not { } organization
            || services.GetService<OrganizationService>() is not { } organizations)
            return true;

        var memberships = await organizations.ListForUserAsync(person, cancellationToken);
        var current = memberships.FirstOrDefault(membership => string.Equals(membership.OrganizationId.ToString("D"), organization, StringComparison.OrdinalIgnoreCase));
        return current is not null && string.Equals(token.GetClaim(OrganizationClaimTypeConstants.Role), $"{organization}:{current.Role}", StringComparison.OrdinalIgnoreCase);
    }
}

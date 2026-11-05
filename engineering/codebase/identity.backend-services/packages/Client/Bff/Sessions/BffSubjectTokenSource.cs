using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Hands a BFF's outbound token exchange the signed-in person's access token, read from their server-side session and
/// fresh, since the cookie's validation refreshes it. Cached per session and active organization.
/// </summary>
/// <param name="accessor">Accessor for the ambient request.</param>
internal sealed class BffSubjectTokenSource(IHttpContextAccessor accessor) : IOAuth2SubjectTokenSource
{
    /// <inheritdoc />
    public async ValueTask<OAuth2SubjectToken?> GetAsync(CancellationToken cancellationToken)
    {
        if (accessor.HttpContext is not { } http)
            return null;

        var session = await http.AuthenticateAsync(BffAuthenticationSchemeConstants.Cookie);
        if (!session.Succeeded
            || session.Properties?.GetTokenValue("access_token") is not { Length: > 0 } token
            || BffSessionReference.From(session.Properties) is not { } reference)
            return null;

        var organization = session.Principal?.FindFirst(OrganizationClaimTypeConstants.Id)?.Value;
        return new OAuth2SubjectToken { Token = token, SessionKey = organization is null ? reference.SessionId : $"{reference.SessionId}:{organization}" };
    }
}

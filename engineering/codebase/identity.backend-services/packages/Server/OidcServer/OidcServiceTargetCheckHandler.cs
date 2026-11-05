using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Exchange;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Answers a service token request for an ungranted target or scope with the code the RFCs name, <c>invalid_target</c> (RFC 8707)
/// or <c>invalid_scope</c> (RFC 6749). It runs right before OpenIddict's own permission checks, which would answer
/// <c>invalid_request</c> for both and still stand behind this one.
/// </summary>
/// <param name="applications">The client registry.</param>
/// <param name="options">The resource registry.</param>
internal sealed class OidcServiceTargetCheckHandler(IOpenIddictApplicationManager applications, IOptions<OidcServerOptions> options) : IOpenIddictServerHandler<ValidateTokenRequestContext>
{
    /// <summary>The handler's registration, ordered right before <see cref="ValidateScopePermissions"/>.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenRequestContext>()
            .UseScopedHandler<OidcServiceTargetCheckHandler>()
            .SetOrder(ValidateScopePermissions.Descriptor.Order - 250)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    /// <inheritdoc />
    public async ValueTask HandleAsync(ValidateTokenRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;
        if (!request.IsClientCredentialsGrantType() && !request.IsTokenExchangeGrantType())
            return;

        var client = await OidcClientProfile.FindAsync(applications, context.ClientId, context.CancellationToken);
        if (client is null)
            return;

        var reachable = request.IsClientCredentialsGrantType() ? client.ServiceAccess.CredentialResources : client.ServiceAccess.ExchangeResources;
        if (OidcServiceTokenGrants.ProblemWith(request, reachable, client, options.Value, out _, out _) is { } problem)
            context.Reject(problem.Error, problem.Description);
    }
}

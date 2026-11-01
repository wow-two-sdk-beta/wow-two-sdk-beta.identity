using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Provides each registered client's look on the hosted pages from <c>Identity:Server:Clients:N:Theme</c> and
/// <c>BackgroundUrl</c>, read live; a client that names neither, or is not registered, has none.
/// </summary>
/// <param name="options">The OIDC server options; <c>Identity:Server</c> reloads live.</param>
internal sealed class OidcClientHostedPageLookSource(IOptionsMonitor<OidcServerOptions> options) : IHostedPageLookSource
{
    /// <inheritdoc />
    public HostedPageLook? Find(string clientId)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        var client = options.CurrentValue.Clients.FirstOrDefault(candidate => string.Equals(candidate.ClientId, clientId, StringComparison.Ordinal));
        if (client is null || (string.IsNullOrWhiteSpace(client.Theme) && string.IsNullOrWhiteSpace(client.BackgroundUrl)))
            return null;

        return new HostedPageLook { Theme = client.Theme, BackgroundUrl = client.BackgroundUrl };
    }
}

/// <summary>Opts the registered clients' looks into the sign-in declaration.</summary>
public static class OidcClientHostedPageLookIdentityBuilderExtensions
{
    /// <summary>
    /// Registers the client registry (<c>Identity:Server:Clients</c>) as the declaration's <see cref="IHostedPageLookSource"/>,
    /// so <c>sign-in-options?clientId=</c> answers that app's own theme and background. Pair with <c>AddOidcServer</c> and
    /// <c>AddSignInDeclaration</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddOidcClientPageLooks(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IHostedPageLookSource, OidcClientHostedPageLookSource>();
        return builder;
    }
}

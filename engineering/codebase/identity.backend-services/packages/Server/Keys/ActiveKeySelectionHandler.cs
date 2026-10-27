using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Protection;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// Picks the signing and encryption certificate per token by date, right after OpenIddict's own choice: of the
/// certificates valid at the server's clock, the one expiring last (<see cref="SigningKeySet.SelectActive"/>).
/// OpenIddict orders its keys once, at boot, so without this a staged certificate would take over only at a restart.
/// </summary>
/// <remarks>Ephemeral keys carry no dates and are left as OpenIddict chose them; so is a token whose certificates are all out of date.</remarks>
internal sealed class ActiveKeySelectionHandler : IOpenIddictServerHandler<GenerateTokenContext>
{
    /// <summary>The handler's registration, ordered right after <see cref="AttachSecurityCredentials"/>.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<GenerateTokenContext>()
            .UseSingletonHandler<ActiveKeySelectionHandler>()
            .SetOrder(AttachSecurityCredentials.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    /// <inheritdoc />
    public ValueTask HandleAsync(GenerateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow();

        if (context.SigningCredentials is { Key: X509SecurityKey }
            && Select(context.Options.SigningCredentials, credentials => credentials.Key, now) is { } signing)
            context.SigningCredentials = signing;

        if (context.EncryptionCredentials is { Key: X509SecurityKey }
            && Select(context.Options.EncryptionCredentials, credentials => credentials.Key, now) is { } encryption)
            context.EncryptionCredentials = encryption;

        return ValueTask.CompletedTask;
    }

    private static TCredentials? Select<TCredentials>(IEnumerable<TCredentials> candidates, Func<TCredentials, SecurityKey> key, DateTimeOffset now)
        where TCredentials : class
    {
        var byCertificate = candidates
            .Select(credentials => (Credentials: credentials, Certificate: (key(credentials) as X509SecurityKey)?.Certificate))
            .Where(candidate => candidate.Certificate is not null)
            .ToList();
        var active = SigningKeySet.SelectActive(byCertificate.Select(candidate => candidate.Certificate!), now);
        return active is null ? null : byCertificate.First(candidate => ReferenceEquals(candidate.Certificate, active)).Credentials;
    }
}

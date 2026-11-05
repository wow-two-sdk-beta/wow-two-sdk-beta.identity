using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Protection;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Keeps service tokens out of the token table: callers ask for one every few minutes and resources verify it offline,
/// so a row would only grow the table. Nothing revokes or redeems such a token; it ends with its short lifetime.
/// </summary>
internal sealed class OidcServiceTokenEntryHandler : IOpenIddictServerHandler<GenerateTokenContext>
{
    /// <summary>The handler's registration, ordered right before <see cref="CreateTokenEntry"/>.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<GenerateTokenContext>()
            .UseSingletonHandler<OidcServiceTokenEntryHandler>()
            .SetOrder(CreateTokenEntry.Descriptor.Order - 250)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    /// <inheritdoc />
    public ValueTask HandleAsync(GenerateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request is { } request && (request.IsClientCredentialsGrantType() || request.IsTokenExchangeGrantType()))
            context.CreateTokenEntry = false;

        return ValueTask.CompletedTask;
    }
}

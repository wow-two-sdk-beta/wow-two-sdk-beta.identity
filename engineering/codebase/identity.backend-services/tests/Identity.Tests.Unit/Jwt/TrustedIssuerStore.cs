using System.Collections.Concurrent;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>Stands in for a product's issuer table: it can gain an issuer while the host runs.</summary>
internal sealed class TrustedIssuerStore
{
    public ConcurrentDictionary<string, TrustedIssuer> Issuers { get; } = new(StringComparer.Ordinal);

    public ConcurrentQueue<string> Asked { get; } = new();

    public Exception? Failure { get; set; }
}

/// <summary>Reads the store; scoped, as the registration makes it.</summary>
internal sealed class StoreIssuerResolver(TrustedIssuerStore store) : ITrustedIssuerResolver
{
    public ValueTask<TrustedIssuer?> ResolveAsync(string issuer, CancellationToken cancellationToken)
    {
        store.Asked.Enqueue(issuer);
        if (store.Failure is { } failure)
            throw failure;

        return ValueTask.FromResult(store.Issuers.GetValueOrDefault(issuer));
    }
}

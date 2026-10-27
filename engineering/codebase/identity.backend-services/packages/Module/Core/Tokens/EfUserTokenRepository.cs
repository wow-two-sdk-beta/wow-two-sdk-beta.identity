using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;

/// <summary>Accesses stored user tokens through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfUserTokenRepository(IdentityDbContext context) : IUserTokenRepository
{
    private DbSet<IdentityUserToken> Tokens => context.Set<IdentityUserToken>();

    /// <inheritdoc />
    public async Task<string?> GetAsync(Guid userId, string loginProvider, string name, CancellationToken cancellationToken = default)
        => (await FindAsync(userId, loginProvider, name, cancellationToken))?.Value;

    /// <inheritdoc />
    public async Task SetAsync(Guid userId, string loginProvider, string name, string value, CancellationToken cancellationToken = default)
    {
        var token = await FindAsync(userId, loginProvider, name, cancellationToken);
        if (token is null)
            Tokens.Add(new IdentityUserToken { UserId = userId, LoginProvider = loginProvider, Name = name, Value = value });
        else
            token.Value = value;

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid userId, string loginProvider, string name, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(userId, loginProvider, name, cancellationToken) is not { } token)
            return;

        Tokens.Remove(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<IdentityUserToken?> FindAsync(Guid userId, string loginProvider, string name, CancellationToken cancellationToken)
        => await Tokens.FindAsync([userId, loginProvider, name], cancellationToken);
}

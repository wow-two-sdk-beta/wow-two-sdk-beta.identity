using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Accesses <c>identity_sign_up_requests</c> through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfSignUpRequestRepository(IdentityDbContext context) : ISignUpRequestRepository
{
    private static readonly TimeSpan PruneGrace = TimeSpan.FromDays(1);

    private DbSet<IdentitySignUpRequest> Requests => context.Set<IdentitySignUpRequest>();

    /// <inheritdoc />
    public async Task AddAsync(IdentitySignUpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var horizon = request.CreatedAt - PruneGrace;
        await Requests.Where(r => r.ExpiresAt < horizon).ExecuteDeleteAsync(cancellationToken);

        Requests.Add(request);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(request).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public Task<IdentitySignUpRequest?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryCompleteAsync(Guid id, Guid? userId, string? failureCode, DateTimeOffset at, CancellationToken cancellationToken = default)
        => await Requests
            .Where(r => r.Id == id && r.CompletedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.CompletedAt, at)
                    .SetProperty(r => r.UserId, userId)
                    .SetProperty(r => r.FailureCode, failureCode)
                    .SetProperty(r => r.Picture, (byte[]?)null),
                cancellationToken) == 1;

    /// <inheritdoc />
    public async Task<bool> TryMarkSignedInAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
        => await Requests
            .Where(r => r.Id == id && r.UserId != null && r.SignedInAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.SignedInAt, at), cancellationToken) == 1;
}

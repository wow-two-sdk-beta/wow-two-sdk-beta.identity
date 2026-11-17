namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>What claiming an app's idempotency key came to (<see cref="IAppNoticeRepository.ClaimAsync"/>).</summary>
/// <param name="ClaimedId">The id of the row this call holds and must complete or release; null when another holds the key.</param>
/// <param name="Existing">The row that holds the key already; null when this call claimed it.</param>
public sealed record AppNoticeClaim(Guid? ClaimedId, IdentityAppNotice? Existing);

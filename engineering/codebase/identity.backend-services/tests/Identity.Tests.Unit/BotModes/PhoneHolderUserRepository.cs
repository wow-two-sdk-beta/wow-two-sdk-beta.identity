using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// Answers only who holds a phone number confirmed, from <see cref="ConfirmedPhones"/>, for the bot's tests that need no
/// database; every other store call fails, since the bot makes none.
/// </summary>
internal sealed class PhoneHolderUserRepository : IUserRepository
{
    private readonly object _gate = new();
    private readonly HashSet<string> _confirmedPhones = new(StringComparer.Ordinal);

    /// <summary>The numbers in E.164 a person holds confirmed.</summary>
    public IReadOnlyCollection<string> ConfirmedPhones
    {
        get
        {
            lock (_gate)
                return [.. _confirmedPhones];
        }
    }

    /// <summary>Makes a person the confirmed holder of <paramref name="phoneNumber"/>.</summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    public void Confirm(string phoneNumber)
    {
        lock (_gate)
            _confirmedPhones.Add(phoneNumber);
    }

    /// <inheritdoc />
    public Task<IdentityUser?> FindByConfirmedPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_confirmedPhones.Contains(phoneNumber)
                ? new IdentityUser { UserName = phoneNumber, PhoneNumber = phoneNumber, IsPhoneNumberConfirmed = true }
                : null);
        }
    }

    /// <inheritdoc />
    public Task CreateAsync(IdentityUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task UpdateAsync(IdentityUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task DeleteAsync(IdentityUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IdentityUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IdentityUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IdentityUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<bool> CountFailedAttemptAsync(IdentityUser user, int maxFailedAttempts, DateTimeOffset lockoutEnd, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public Task ResetFailedAttemptsAsync(IdentityUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

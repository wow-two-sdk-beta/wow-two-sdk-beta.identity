using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>
/// Provides an account's own phone beside its trusted contacts: whether a session was signed in with it, and the bot's
/// <c>add-phone</c> start link that adds a first phone from any session, or changes an existing one from a session signed
/// in with it. The shared contact in the bot proves the number, which becomes the account's confirmed phone.
/// </summary>
/// <remarks>
///   - an own phone is a confirmed one; a session is signed in with it when it went through no contact and the sign-in log
///     row that started it is a <c>phone_code</c> or <c>phone_contact</c> sign-in (<c>Identity:SignInLog</c> keeps it)
///   - a link carries the session that asked for it, so its completion checks the rule again: an account that gained an own
///     phone meanwhile is changed only from a session signed in with it
///   - a first phone ends no session, the asking browser's included
///   - a changed number rotates the stamp, ending every session at once and telling services, since a session signed in with the
///     old number no longer proves the account; the asking browser signs in again, with the new number
///   - a changed number closes the account's requests waiting for a contact's press, so no press signs in whoever held the old one
/// </remarks>
/// <param name="repository">The sessions and the sign-in log.</param>
/// <param name="users">Finds the person holding a number confirmed.</param>
/// <param name="accounts">Reads and updates the account.</param>
/// <param name="phones">The allowed countries.</param>
/// <param name="telegram">Makes the start link.</param>
/// <param name="devices">The requests a changed number closes.</param>
/// <param name="time">The clock a session's end is judged against.</param>
public sealed class OwnPhoneService(
    ITrustedContactRepository repository,
    IUserRepository users,
    UserAccountService accounts,
    PhoneNumberPolicy phones,
    TelegramLinkService telegram,
    IDeviceConfirmationRepository devices,
    TimeProvider time)
{
    /// <summary>Whether <paramref name="user"/> holds an own phone: a confirmed number.</summary>
    /// <param name="user">The person.</param>
    public static bool HasOwnPhone(IdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsPhoneNumberConfirmed && !string.IsNullOrWhiteSpace(user.PhoneNumber);
    }

    /// <summary>
    /// Whether <paramref name="sessionId"/> is a live session of <paramref name="user"/> signed in with their own phone:
    /// through no trusted contact, by a phone code to their own chat or an invitation sign-up's shared contact.
    /// </summary>
    /// <param name="user">The person.</param>
    /// <param name="sessionId">The cookie's <c>sid</c>; null (a bearer token) is never one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsOwnPhoneSessionAsync(IdentityUser user, Guid? sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (sessionId is not { } id || !HasOwnPhone(user))
            return false;

        var session = await repository.FindSessionAsync(id, cancellationToken);
        return session is { RevokedAt: null, TrustedContactUserId: null }
            && session.UserId == user.Id
            && session.ExpiresAt > time.GetUtcNow()
            && await repository.IsOwnPhoneSignInAsync(user.Id, id, cancellationToken);
    }

    /// <summary>Whether the session may set the account's phone: an account with none adds one from any session.</summary>
    /// <param name="user">The person.</param>
    /// <param name="sessionId">The cookie's <c>sid</c>, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> CanChangeAsync(IdentityUser user, Guid? sessionId, CancellationToken cancellationToken = default)
        => !HasOwnPhone(user) || await IsOwnPhoneSessionAsync(user, sessionId, cancellationToken);

    /// <summary>Makes the bot's <c>add-phone</c> start link for <paramref name="user"/>; see <see cref="OwnPhoneService"/>.</summary>
    /// <param name="user">The signed-in person.</param>
    /// <param name="sessionId">The cookie's <c>sid</c>; null for a bearer token, whose link only adds a first phone.</param>
    /// <param name="origin">Where the request came from, for rate limiting; null when unknown.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OwnPhoneLinkResult> CreateLinkAsync(IdentityUser user, Guid? sessionId, string? origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!telegram.IsEnabled)
            return new OwnPhoneLinkResult { Status = OwnPhoneLinkStatus.Unavailable };
        if (!await CanChangeAsync(user, sessionId, cancellationToken))
            return new OwnPhoneLinkResult { Status = OwnPhoneLinkStatus.MainPhoneRequired };
        if (await telegram.IsRateLimitedAsync(phoneNumber: null, origin, cancellationToken))
            return new OwnPhoneLinkResult { Status = OwnPhoneLinkStatus.RateLimited };

        var url = await telegram.CreateStartLinkAsync(phoneNumber: null, TelegramLinkPurposeConstants.AddPhone, sessionId ?? user.Id, origin, cancellationToken);
        return new OwnPhoneLinkResult { Status = OwnPhoneLinkStatus.Created, TelegramLinkUrl = url };
    }

    /// <summary>
    /// Sets the number a shared contact just proved as the account's confirmed phone, for the <c>add-phone</c> link made
    /// with <paramref name="referenceId"/>; the bot's reply key comes back.
    /// </summary>
    /// <param name="referenceId">The session that asked for the link, or the account itself for a bearer token's.</param>
    /// <param name="phoneNumber">The proven number in E.164.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text the bot answers with (<see cref="TelegramBotMessageKeyConstants"/>).</returns>
    public async Task<string> CompleteAsync(Guid referenceId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        IdentityUser? user;
        var mayChange = false;
        if (await repository.FindSessionAsync(referenceId, cancellationToken) is { } session)
        {
            user = await accounts.FindByIdAsync(session.UserId, cancellationToken);
            if (user is null || session.RevokedAt is not null || session.ExpiresAt <= time.GetUtcNow())
                return TelegramBotMessageKeyConstants.AddPhoneFailed;
            mayChange = await IsOwnPhoneSessionAsync(user, session.Id, cancellationToken);
        }
        else
        {
            user = await accounts.FindByIdAsync(referenceId, cancellationToken);
        }

        if (user is null)
            return TelegramBotMessageKeyConstants.AddPhoneFailed;
        if (!phones.IsAllowed(phoneNumber))
            return TelegramBotMessageKeyConstants.CountryNotAllowed;

        var holder = await users.FindByConfirmedPhoneNumberAsync(phoneNumber, cancellationToken);
        if (holder is not null && holder.Id != user.Id)
            return TelegramBotMessageKeyConstants.AddPhoneTaken;
        if (holder is null && HasOwnPhone(user) && !mayChange)
            return TelegramBotMessageKeyConstants.AddPhoneFailed;

        var isChange = HasOwnPhone(user) && !string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal);
        user.PhoneNumber = phoneNumber;
        user.IsPhoneNumberConfirmed = true;
        var updated = await accounts.UpdateAsync(user, cancellationToken);
        if (!updated.IsSuccess)
            return TelegramBotMessageKeyConstants.AddPhoneFailed;

        if (isChange)
        {
            await devices.RemoveRequestsOfAsync(user.Id, cancellationToken);
            await accounts.RotateSecurityStampAsync(user, cancellationToken);
        }

        return TelegramBotMessageKeyConstants.AddPhoneDone;
    }
}

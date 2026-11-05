using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Models;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>
/// Provides trusted contacts: another person of the instance whose confirmed, bot-linked phone receives an account's
/// sign-in codes, with the same power as the account's own phone. An invitation names one; a sign-up through it, a sign-in
/// by the account's number to a contact and a contact's own number choosing an account all go through the bot; the account
/// with its own phone hears of each sign-in through a contact, with a one-press revoke.
/// </summary>
/// <remarks>
///   - a person qualifies as a contact only while their phone is confirmed and linked to the current bot
///   - an account holding an instance role has no contact in slice 1: none is recorded for it, and none signs it in
///   - a code to a contact is keyed by the contact (<see cref="CodeScope"/>): the contact's 30 s window and failure budget
///     count every code they receive, whichever account it is for; the code itself is bound to its target (one account, or
///     one sign-up), so it verifies for nothing else. A sign-in by an account's number also keeps that number's own window
///     and failure budget.
///   - revoking a contact, or the contact leaving, ends every session started through them
/// </remarks>
/// <param name="repository">The contacts, and the roles the rules read.</param>
/// <param name="options">The switch and the cap; <c>Identity:TrustedContacts</c> reloads live.</param>
/// <param name="users">Finds the person holding a confirmed number.</param>
/// <param name="accounts">Reads people by id.</param>
/// <param name="phones">The number rules and the allowed countries.</param>
/// <param name="telegram">The chats numbers are linked to.</param>
/// <param name="links">The links' Telegram users, for the notice's button.</param>
/// <param name="bot">The bot's name and default culture.</param>
/// <param name="messages">The bot's wording.</param>
/// <param name="services">The scope's provider, for the notice sender, which needs the bot's token and so is resolved only to send.</param>
/// <param name="otp">Creates and spends the codes and account choice tokens.</param>
/// <param name="otpOptions">The code's shape and lifetime, and the app's name.</param>
/// <param name="hasher">Keys the opaque target ids.</param>
/// <param name="channels">The <c>telegram</c> delivery channel.</param>
/// <param name="time">The clock rows are stamped with.</param>
/// <param name="logger">Logs a notice Telegram refused.</param>
/// <param name="sessions">The sessions slice, to end those started through a revoked contact; null without it.</param>
/// <param name="organizations">The organizations, to check an invitation's contact is a member; null without them.</param>
/// <param name="pictures">The pictures, for the account choice; null without them.</param>
/// <param name="pictureOptions">The picture link template; null without pictures.</param>
/// <param name="signInLog">The sign-in log, for the app a notice names; null without it.</param>
public sealed partial class TrustedContactService(
    ITrustedContactRepository repository,
    IOptionsMonitor<TrustedContactOptions> options,
    IUserRepository users,
    UserAccountService accounts,
    PhoneNumberPolicy phones,
    TelegramLinkService telegram,
    ITelegramLinkRepository links,
    IOptionsMonitor<TelegramBotOptions> bot,
    TelegramBotMessages messages,
    IServiceProvider services,
    IOtpService otp,
    OtpOptions otpOptions,
    OtpCodeHasher hasher,
    IOtpDeliveryHandlerFactory channels,
    TimeProvider time,
    ILogger<TrustedContactService> logger,
    UserSessionService? sessions = null,
    OrganizationService? organizations = null,
    IProfilePictureStore? pictures = null,
    IOptionsMonitor<ProfilePictureOptions>? pictureOptions = null,
    SignInLogService? signInLog = null)
{
    /// <summary>The OTP scope of every code a trusted contact receives; the subject is the contact.</summary>
    public const string CodeScope = "identity.trusted-contact";

    /// <summary>The OTP scope of account choice tokens; the subject is the contact who verified.</summary>
    public const string ChoiceScope = "identity.trusted-contact.choice";

    /// <summary>The action of the notice's "Revoke trust" button (<see cref="TrustedContactRevokeHandler"/>).</summary>
    public const string RevokeAction = "trusted-contacts.revoke";

    /// <summary>How long an account choice token works.</summary>
    public static readonly TimeSpan ChoiceLifetime = TimeSpan.FromMinutes(10);

    private const int ChoiceTokenLength = 12;

    /// <summary>Whether trusted contacts run: <c>Identity:TrustedContacts:IsEnabled</c> and the instance's bot are on.</summary>
    public bool IsEnabled => options.CurrentValue.IsEnabled && telegram.IsEnabled;

    /// <summary>Whether a code can reach <paramref name="contact"/> now: their phone is confirmed and linked to the current bot.</summary>
    /// <param name="contact">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsDeliverableAsync(IdentityUser contact, CancellationToken cancellationToken = default)
        => await ChatOfAsync(contact, cancellationToken) is not null;

    /// <summary>Whether <paramref name="userId"/> may have trusted contacts: it holds no instance role.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> MayHaveContactsAsync(Guid userId, CancellationToken cancellationToken = default)
        => !await repository.HoldsInstanceRoleAsync(userId, cancellationToken);

    /// <summary>
    /// Checks the contact an invitation's maker names: trusted contacts are on, and the contact is the maker or another
    /// member of the organization whose phone is confirmed and linked to the bot. Null names none and passes.
    /// </summary>
    /// <param name="organization">The organization the invitation joins.</param>
    /// <param name="inviter">The owner or admin making it.</param>
    /// <param name="contactUserId">The contact named, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> CheckInvitationContactAsync(IdentityOrganization organization, IdentityUser inviter, Guid? contactUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(inviter);
        if (contactUserId is not { } contactId)
            return IdentityResult.Success;
        if (!IsEnabled)
            return Failure(TrustedContactErrorCodeConstants.TrustedContactsOff, "Trusted contacts are off on this instance.");

        var contact = contactId == inviter.Id ? inviter : await accounts.FindByIdAsync(contactId, cancellationToken);
        var isMember = contact is not null
            && (contact.Id == inviter.Id || (organizations is not null && await organizations.GetRoleAsync(organization, contact, cancellationToken) is not null));
        if (!isMember)
            return Failure(TrustedContactErrorCodeConstants.InvalidTrustedContact, "The trusted contact must be you or another member of the organization.");

        return await IsDeliverableAsync(contact!, cancellationToken)
            ? IdentityResult.Success
            : Failure(TrustedContactErrorCodeConstants.TrustedContactUnavailable, "The trusted contact needs a confirmed phone linked to the bot.");
    }

    /// <summary>
    /// Records <paramref name="contactUserId"/> as a trusted contact of <paramref name="userId"/>, within the caller's
    /// transaction when one is open; refused for oneself, an account holding an instance role, or one at
    /// <c>Identity:TrustedContacts:MaxPerPerson</c>. Recording a pair again succeeds and changes nothing.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="contactUserId">The contact.</param>
    /// <param name="createdByUserId">Who named the contact, such as the invitation's maker.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> RecordAsync(Guid userId, Guid contactUserId, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        if (userId == contactUserId)
            return Failure(TrustedContactErrorCodeConstants.InvalidTrustedContact, "A person cannot be their own trusted contact.");
        if (await repository.FindAsync(userId, contactUserId, cancellationToken) is not null)
            return IdentityResult.Success;
        if (!await MayHaveContactsAsync(userId, cancellationToken))
            return Failure(TrustedContactErrorCodeConstants.InstanceRoleHolder, "An account holding an instance role cannot have a trusted contact.");
        if ((await repository.ListContactsAsync(userId, cancellationToken)).Count >= options.CurrentValue.MaxPerPerson)
            return Failure(TrustedContactErrorCodeConstants.TooManyTrustedContacts, $"An account has at most {options.CurrentValue.MaxPerPerson} trusted contacts.");

        await repository.AddAsync(
            new IdentityTrustedContact { UserId = userId, ContactUserId = contactUserId, CreatedByUserId = createdByUserId, CreatedAt = time.GetUtcNow() },
            cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>The trusted contacts of <paramref name="user"/>, oldest first.</summary>
    /// <param name="user">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<TrustedContactModel>> ListContactsAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var listed = new List<TrustedContactModel>();
        foreach (var row in await repository.ListContactsAsync(user.Id, cancellationToken))
        {
            if (await accounts.FindByIdAsync(row.ContactUserId, cancellationToken) is not { } contact)
                continue;

            listed.Add(new TrustedContactModel
            {
                UserId = contact.Id,
                DisplayName = NameOf(contact),
                PhoneHint = PersonalDataMasks.MaskSubject(contact.IsPhoneNumberConfirmed ? contact.PhoneNumber : null),
                IsDeliverable = await IsDeliverableAsync(contact, cancellationToken),
                CreatedAt = row.CreatedAt,
            });
        }

        return listed;
    }

    /// <summary>The accounts trusting <paramref name="contact"/>, oldest first, each saying whether the contact may leave it.</summary>
    /// <param name="contact">The contact.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<TrustingAccountModel>> ListTrustingAsync(IdentityUser contact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        var listed = new List<TrustingAccountModel>();
        foreach (var row in await repository.ListTrustingAsync(contact.Id, cancellationToken))
        {
            if (await accounts.FindByIdAsync(row.UserId, cancellationToken) is not { } account)
                continue;

            listed.Add(new TrustingAccountModel
            {
                UserId = account.Id,
                DisplayName = NameOf(account),
                CreatedAt = row.CreatedAt,
                CanLeave = await CanLeaveAsync(account, cancellationToken),
            });
        }

        return listed;
    }

    /// <summary>The trusted contacts of each of <paramref name="userIds"/>, by account, oldest first.</summary>
    /// <param name="userIds">The accounts, such as an organization's members.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<IdentityUser>>> ContactsOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var rows = await repository.ListContactsOfAsync(userIds, cancellationToken);
        var people = new Dictionary<Guid, IdentityUser>();
        foreach (var id in rows.Select(row => row.ContactUserId).Distinct())
        {
            if (await accounts.FindByIdAsync(id, cancellationToken) is { } person)
                people[id] = person;
        }

        return rows
            .Where(row => people.ContainsKey(row.ContactUserId))
            .GroupBy(row => row.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<IdentityUser>)[.. group.Select(row => people[row.ContactUserId])]);
    }

    /// <summary>Revokes <paramref name="contactUserId"/> as a contact of <paramref name="account"/>, ending the sessions started through them.</summary>
    /// <param name="account">The account.</param>
    /// <param name="contactUserId">The contact.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the account trusted them.</returns>
    public async Task<bool> RevokeAsync(IdentityUser account, Guid contactUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!await repository.TryRemoveAsync(account.Id, contactUserId, cancellationToken))
            return false;

        if (sessions is not null)
            await sessions.RevokeThroughContactAsync(account.Id, contactUserId, cancellationToken);
        return true;
    }

    /// <summary>
    /// Takes <paramref name="contact"/> off the contacts of <paramref name="accountId"/>, ending the sessions started through
    /// them; refused while they are the last contact of an account with no own phone.
    /// </summary>
    /// <param name="contact">The contact leaving.</param>
    /// <param name="accountId">The account they leave.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TrustedContactLeaveStatus> LeaveAsync(IdentityUser contact, Guid accountId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        if (await repository.FindAsync(accountId, contact.Id, cancellationToken) is null
            || await accounts.FindByIdAsync(accountId, cancellationToken) is not { } account)
            return TrustedContactLeaveStatus.NotFound;
        if (!await CanLeaveAsync(account, cancellationToken))
            return TrustedContactLeaveStatus.LastContact;

        return await RevokeAsync(account, contact.Id, cancellationToken) ? TrustedContactLeaveStatus.Left : TrustedContactLeaveStatus.NotFound;
    }

    /// <summary>
    /// The contacts a sign-in by <paramref name="phoneNumber"/> may send its code to instead of the number's own chat: the
    /// deliverable contacts of the account holding it confirmed, as masked labels and opaque ids. Empty when no account holds
    /// it, it holds an instance role, or trusted contacts are off.
    /// </summary>
    /// <param name="phoneNumber">The number as typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<TrustedContactTarget>> ListTargetsAsync(string? phoneNumber, CancellationToken cancellationToken = default)
    {
        var account = await AccountOfAsync(phoneNumber, cancellationToken);
        if (account is null)
            return [];

        var targets = new List<TrustedContactTarget>();
        foreach (var contact in await DeliverableContactsAsync(account, cancellationToken))
        {
            targets.Add(new TrustedContactTarget { Id = TargetIdOf(account.Id, contact.Id), Label = LabelOf(contact) });
        }

        return targets;
    }

    /// <summary>
    /// The contacts who can answer for <paramref name="account"/> now: its contacts whose phones are confirmed and linked to the
    /// bot. Empty while trusted contacts are off or it holds an instance role.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<IdentityUser>> ListDeliverableContactsAsync(IdentityUser account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        return IsEnabled && await MayHaveContactsAsync(account.Id, cancellationToken)
            ? await DeliverableContactsAsync(account, cancellationToken)
            : [];
    }

    /// <summary>
    /// Sends a sign-in code for the account holding <paramref name="phoneNumber"/> to the contact <paramref name="targetId"/>
    /// names (<see cref="ListTargetsAsync"/>), worded for the contact with the account and the app. The number's own window
    /// and failure budget, and the contact's, gate it.
    /// </summary>
    /// <param name="phoneNumber">The account's number as typed.</param>
    /// <param name="targetId">One of the ids <see cref="ListTargetsAsync"/> gave.</param>
    /// <param name="origin">Where the request came from, such as the client address; null limits by number and contact only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TrustedContactSendStatus> SendSignInCodeAsync(string? phoneNumber, string? targetId, string? origin, CancellationToken cancellationToken = default)
    {
        var phone = PhoneNumberPolicy.Normalize(phoneNumber);
        if (phone is null)
            return TrustedContactSendStatus.InvalidNumber;
        if (!phones.IsAllowed(phone))
            return TrustedContactSendStatus.CountryNotAllowed;

        var account = await AccountOfAsync(phone, cancellationToken);
        if (account is null || string.IsNullOrWhiteSpace(targetId))
            return TrustedContactSendStatus.NotFound;

        IdentityUser? contact = null;
        foreach (var row in await repository.ListContactsAsync(account.Id, cancellationToken))
        {
            if (string.Equals(TargetIdOf(account.Id, row.ContactUserId), targetId.Trim(), StringComparison.Ordinal))
                contact = await accounts.FindByIdAsync(row.ContactUserId, cancellationToken);
        }

        if (contact is null)
            return TrustedContactSendStatus.NotFound;
        if (await otp.IsRateLimitedAsync(phone, Phones.UserPhoneService.SignInScope, origin, cancellationToken))
            return TrustedContactSendStatus.RateLimited;

        return await SendCodeAsync(
            contact,
            AccountBinding(account.Id),
            TelegramBotMessageKeyConstants.ContactSignInCode,
            AccountNameOf(account),
            origin,
            cancellationToken);
    }

    /// <summary>
    /// Sends the code of the invitation sign-up <paramref name="signUpId"/> to <paramref name="contact"/>, the contact its
    /// invitation names, worded with the name the person gave and the app; the contact's window and failure budget gate it.
    /// </summary>
    /// <param name="contact">The invitation's contact.</param>
    /// <param name="signUpId">The waiting sign-up the code finishes.</param>
    /// <param name="accountName">The name the person signing up gave; null when none.</param>
    /// <param name="origin">Where the request came from; null limits by contact only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<TrustedContactSendStatus> SendSignUpCodeAsync(IdentityUser contact, Guid signUpId, string? accountName, string? origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return SendCodeAsync(contact, SignUpBinding(signUpId), TelegramBotMessageKeyConstants.ContactSignUpCode, accountName ?? "—", origin, cancellationToken);
    }

    /// <summary>Whether a code went to <paramref name="contactUserId"/> moments ago, or the contact spent their failure budget.</summary>
    /// <param name="contactUserId">The contact.</param>
    /// <param name="origin">Where the request came from; null checks the contact only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> IsContactRateLimitedAsync(Guid contactUserId, string? origin, CancellationToken cancellationToken = default)
        => otp.IsRateLimitedAsync(ContactSubject(contactUserId), CodeScope, origin, cancellationToken);

    /// <summary>Spends <paramref name="code"/>, the code of the sign-up <paramref name="signUpId"/> sent to <paramref name="contactUserId"/>.</summary>
    /// <param name="contactUserId">The invitation's contact.</param>
    /// <param name="signUpId">The waiting sign-up.</param>
    /// <param name="code">The code as typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> VerifySignUpCodeAsync(Guid contactUserId, Guid signUpId, string? code, CancellationToken cancellationToken = default)
        => !string.IsNullOrWhiteSpace(code)
            && (await otp.VerifyBoundAsync(ContactSubject(contactUserId), code.Trim(), CodeScope, SignUpBinding(signUpId), cancellationToken)).IsSuccess;

    /// <summary>
    /// Spends <paramref name="code"/> as a sign-in code one of <paramref name="account"/>'s deliverable contacts received for
    /// it; the contact it went to, or null when it is none of theirs (or the account may have no contact, or trusted contacts
    /// are off). Each contact holding a pending code counts the try.
    /// </summary>
    /// <param name="account">The account holding the number signed in with.</param>
    /// <param name="code">The code as typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Guid?> VerifySignInCodeAsync(IdentityUser account, string? code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!IsEnabled || string.IsNullOrWhiteSpace(code) || !await MayHaveContactsAsync(account.Id, cancellationToken))
            return null;

        foreach (var contact in await DeliverableContactsAsync(account, cancellationToken))
        {
            if ((await otp.VerifyBoundAsync(ContactSubject(contact.Id), code.Trim(), CodeScope, AccountBinding(account.Id), cancellationToken)).IsSuccess)
                return contact.Id;
        }

        return null;
    }

    /// <summary>
    /// The account choice for <paramref name="contact"/>, whose own code just verified: a single-use token and their own
    /// account beside each account trusting them; null when nobody trusts them (they sign straight in) or trusted contacts
    /// are off.
    /// </summary>
    /// <param name="contact">The person who signed in with their own number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AccountChoice?> OfferAccountsAsync(IdentityUser contact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        if (!IsEnabled)
            return null;

        var trusting = await TrustingAccountsAsync(contact, cancellationToken);
        if (trusting.Count == 0)
            return null;

        var created = await otp.CreateAsync(
            ChoiceSubject(contact.Id),
            ChoiceScope,
            new OtpCodeSpec { Kind = OtpCodeKind.Alphanumeric, Length = ChoiceTokenLength, Lifetime = ChoiceLifetime },
            cancellationToken);
        if (!created.IsSuccess)
            return null;

        List<AccountChoiceOption> choices = [await OptionOfAsync(contact, isSelf: true, cancellationToken)];
        foreach (var account in trusting)
            choices.Add(await OptionOfAsync(account, isSelf: false, cancellationToken));

        return new AccountChoice { Token = contact.Id.ToString("N", CultureInfo.InvariantCulture) + "." + created.Code, Accounts = choices };
    }

    /// <summary>
    /// Spends an account choice <paramref name="token"/> for <paramref name="userId"/>: the contact's own account, or one
    /// still trusting them; null when the token is unknown, spent or expired, or does not offer the account. A choice the
    /// token cannot offer leaves it unspent.
    /// </summary>
    /// <param name="token">The token <see cref="OfferAccountsAsync"/> gave.</param>
    /// <param name="userId">The account picked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AccountChoiceRedemption?> ChooseAsync(string? token, Guid userId, CancellationToken cancellationToken = default)
    {
        var dot = token?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        if (!IsEnabled || token is null || dot <= 0 || !Guid.TryParseExact(token[..dot], "N", out var contactId))
            return null;

        var contact = await accounts.FindByIdAsync(contactId, cancellationToken);
        if (contact is null)
            return null;

        IdentityUser? chosen = userId == contact.Id
            ? contact
            : (await TrustingAccountsAsync(contact, cancellationToken)).FirstOrDefault(account => account.Id == userId);
        if (chosen is null)
            return null;
        if (!(await otp.VerifyAsync(ChoiceSubject(contact.Id), token[(dot + 1)..], ChoiceScope, cancellationToken)).IsSuccess)
            return null;

        return new AccountChoiceRedemption { User = chosen, TrustedContactUserId = chosen.Id == contact.Id ? null : contact.Id };
    }

    /// <summary>
    /// Tells <paramref name="account"/>'s own chat that <paramref name="contactUserId"/> signed in to it, with a one-press
    /// "Revoke trust" only its own Telegram user may press; nothing is sent while the account has no own phone linked to
    /// the bot. A refused notice is logged, never thrown.
    /// </summary>
    /// <param name="account">The account signed in to.</param>
    /// <param name="contactUserId">The contact the sign-in went through.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether a notice went out.</returns>
    public async Task<bool> NotifySignInAsync(IdentityUser account, Guid contactUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!OwnPhoneService.HasOwnPhone(account)
            || await links.FindLinkAsync(bot.CurrentValue.NormalizedBotUserName, account.PhoneNumber!, cancellationToken) is not { } link
            || await accounts.FindByIdAsync(contactUserId, cancellationToken) is not { } contact)
            return false;

        var culture = messages.CultureOf(null);
        var now = time.GetUtcNow();
        var text = messages.Format(TelegramBotMessageKeyConstants.ContactSignInNotice, culture, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["contact"] = AccountNameOf(contact),
            ["app"] = string.IsNullOrWhiteSpace(signInLog?.ClientId) ? otpOptions.Messages.AppName : signInLog.ClientId,
            ["time"] = now.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
        });
        var sent = await services.GetRequiredService<ITelegramNoticeSender>().SendAsync(
            new TelegramNotice
            {
                Kind = TelegramMessageKindConstants.ContactSignIn,
                UserId = account.Id,
                ChatId = link.ChatId,
                Text = text,
                Culture = culture.Name,
                Buttons =
                [
                    new TelegramNoticeButton
                    {
                        Label = messages.Text(TelegramBotMessageKeyConstants.RevokeTrustButton, culture),
                        Action = RevokeAction,
                        Payload = contactUserId.ToString("N", CultureInfo.InvariantCulture),
                        AllowedTelegramUserId = link.TelegramUserId,
                    },
                ],
            },
            cancellationToken);
        if (!sent.IsSent)
            LogNoticeRefused(logger, sent.FailureReason);
        return sent.IsSent;
    }

    /// <summary>The name a person shows a contact or an account owner: their display name, else their masked phone or user name.</summary>
    /// <param name="person">The person.</param>
    internal static string? NameOf(IdentityUser person)
        => string.IsNullOrWhiteSpace(person.DisplayName)
            ? PersonalDataMasks.MaskSubject(person.PhoneNumber) ?? PersonalDataMasks.MaskSubject(person.UserName)
            : person.DisplayName;

    private async Task<TrustedContactSendStatus> SendCodeAsync(IdentityUser contact, string binding, string key, string accountName, string? origin, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return TrustedContactSendStatus.NotFound;
        if (await ChatOfAsync(contact, cancellationToken) is not { } chatId)
            return TrustedContactSendStatus.Unavailable;

        var created = await otp.CreateBoundAsync(ContactSubject(contact.Id), CodeScope, otpOptions.DefaultSpec, origin, binding, cancellationToken);
        if (!created.IsSuccess)
            return TrustedContactSendStatus.RateLimited;

        var culture = messages.CultureOf(null);
        var text = messages.Format(key, culture, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = created.Code!,
            ["account"] = accountName,
            ["app"] = otpOptions.Messages.AppName,
            ["minutes"] = ((int)Math.Ceiling(otpOptions.CodeLifetime.TotalMinutes)).ToString(CultureInfo.InvariantCulture),
        });
        var channel = channels.Create(OtpChannelNameConstants.Telegram)
            ?? throw new InvalidOperationException("Trusted contacts need the telegram OTP channel; call .AddTelegramLinks().");
        var delivered = await channel.SendAsync(
            new OtpDeliveryEnvelopeModel
            {
                DeliveryAddress = chatId.ToString(CultureInfo.InvariantCulture),
                Code = created.Code!,
                Scope = CodeScope,
                Text = text,
                Lifetime = otpOptions.CodeLifetime,
                Culture = culture.Name,
            },
            cancellationToken);
        if (delivered.IsSuccess)
            return TrustedContactSendStatus.CodeSent;

        // The chat refused it (the bot was blocked): no code the contact never saw stays open to guessing.
        if (created.CodeId is { } codeId)
            await otp.DiscardAsync(codeId, cancellationToken);
        return TrustedContactSendStatus.Unavailable;
    }

    /// <summary>The account holding <paramref name="phoneNumber"/> confirmed, when it may sign in through a contact now.</summary>
    private async Task<IdentityUser?> AccountOfAsync(string? phoneNumber, CancellationToken cancellationToken)
    {
        var phone = PhoneNumberPolicy.Normalize(phoneNumber);
        if (!IsEnabled || phone is null)
            return null;

        var account = await users.FindByConfirmedPhoneNumberAsync(phone, cancellationToken);
        return account is not null && await MayHaveContactsAsync(account.Id, cancellationToken) ? account : null;
    }

    private async Task<IReadOnlyList<IdentityUser>> DeliverableContactsAsync(IdentityUser account, CancellationToken cancellationToken)
    {
        var contacts = new List<IdentityUser>();
        foreach (var row in await repository.ListContactsAsync(account.Id, cancellationToken))
        {
            if (await accounts.FindByIdAsync(row.ContactUserId, cancellationToken) is { } contact && await IsDeliverableAsync(contact, cancellationToken))
                contacts.Add(contact);
        }

        return contacts;
    }

    /// <summary>The accounts trusting <paramref name="contact"/> that may sign in through them now.</summary>
    private async Task<IReadOnlyList<IdentityUser>> TrustingAccountsAsync(IdentityUser contact, CancellationToken cancellationToken)
    {
        if (!await IsDeliverableAsync(contact, cancellationToken))
            return [];

        var trusting = new List<IdentityUser>();
        foreach (var row in await repository.ListTrustingAsync(contact.Id, cancellationToken))
        {
            if (await accounts.FindByIdAsync(row.UserId, cancellationToken) is { } account && await MayHaveContactsAsync(account.Id, cancellationToken))
                trusting.Add(account);
        }

        return trusting;
    }

    private async Task<bool> CanLeaveAsync(IdentityUser account, CancellationToken cancellationToken)
        => OwnPhoneService.HasOwnPhone(account) || (await repository.ListContactsAsync(account.Id, cancellationToken)).Count > 1;

    private async Task<long?> ChatOfAsync(IdentityUser person, CancellationToken cancellationToken)
        => person.IsPhoneNumberConfirmed && PhoneNumberPolicy.Normalize(person.PhoneNumber) is { } phone
            ? await telegram.FindChatAsync(phone, cancellationToken)
            : null;

    private async Task<AccountChoiceOption> OptionOfAsync(IdentityUser person, bool isSelf, CancellationToken cancellationToken)
    {
        string? pictureUrl = null;
        if (pictures is not null && pictureOptions is not null && await pictures.FindVersionAsync(person.Id, cancellationToken) is { } version)
            pictureUrl = pictureOptions.CurrentValue.LinkOf(person.Id, version);

        return new AccountChoiceOption { UserId = person.Id, DisplayName = NameOf(person), PictureUrl = pictureUrl, IsSelf = isSelf };
    }

    /// <summary>A contact's masked label, as the sign-in page shows it: name and phone, or the phone alone.</summary>
    /// <param name="contact">The contact.</param>
    internal static string LabelOf(IdentityUser contact)
    {
        var name = PersonalDataMasks.MaskName(contact.DisplayName);
        var phone = PersonalDataMasks.MaskSubject(contact.PhoneNumber);
        return name is null ? phone! : $"{name} · {phone}";
    }

    /// <summary>The name a contact's message gives the account: its display name, else its number or user name.</summary>
    /// <param name="person">The account.</param>
    internal static string AccountNameOf(IdentityUser person)
        => !string.IsNullOrWhiteSpace(person.DisplayName) ? person.DisplayName : person.PhoneNumber ?? person.UserName ?? person.Id.ToString();

    /// <summary>The opaque id of a sign-in target: a keyed digest of the account and the contact, the same on every instance.</summary>
    private string TargetIdOf(Guid accountId, Guid contactUserId)
        => hasher.Subject(string.Create(CultureInfo.InvariantCulture, $"trusted-contact-target:{accountId:N}:{contactUserId:N}"))[..32];

    private static string ContactSubject(Guid contactUserId) => string.Create(CultureInfo.InvariantCulture, $"trusted-contact:{contactUserId:N}");

    private static string ChoiceSubject(Guid contactUserId) => string.Create(CultureInfo.InvariantCulture, $"account-choice:{contactUserId:N}");

    private static string AccountBinding(Guid accountId) => string.Create(CultureInfo.InvariantCulture, $"account:{accountId:N}");

    private static string SignUpBinding(Guid signUpId) => string.Create(CultureInfo.InvariantCulture, $"sign-up:{signUpId:N}");

    private static IdentityResult Failure(string code, string description) => IdentityUserExtensions.Failure(code, description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The bot's notice of a sign-in through a trusted contact was not sent: {Reason}")]
    private static partial void LogNoticeRefused(ILogger logger, string? reason);
}

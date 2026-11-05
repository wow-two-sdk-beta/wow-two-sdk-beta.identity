using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>
/// Holds a sign-in by an account's own code on a browser it hasn't used until a trusted contact presses Approve (developer,
/// 2026-11-05: "any new browser or device"). A recycled SIM logged in to the old Telegram account gets the code, and this is
/// what still stops it. Spec: <c>product/features/trusted-contacts.md</c> § New devices.
/// </summary>
/// <remarks>
///   - an account with no deliverable contact is never asked, since nobody could answer
///   - a sign-in through a contact asks nobody, and its browser becomes known: the contact was in the loop
///   - a new request replaces the account's open one, and the notice sender spends the older notice's buttons
///   - a bearer client keeps no device cookie, so it is asked every time
///   - the notice names the instance's app, never the request's <c>clientId</c>, which whoever is signing in writes
/// </remarks>
/// <param name="repository">The known browsers and waiting requests.</param>
/// <param name="contacts">Whether a presser is still a contact.</param>
/// <param name="trusted">The contacts who can answer now.</param>
/// <param name="options">The switch; <c>Identity:TrustedContacts</c> reloads live.</param>
/// <param name="accounts">Reads people by id.</param>
/// <param name="links">The contacts' chats and Telegram users.</param>
/// <param name="bot">The bot's name.</param>
/// <param name="messages">The bot's wording.</param>
/// <param name="services">The scope's provider, for the notice sender, which needs the bot's token and so is resolved only to send.</param>
/// <param name="otpOptions">The app's name.</param>
/// <param name="time">The clock rows are stamped with.</param>
/// <param name="logger">Logs a notice Telegram refused.</param>
public sealed partial class DeviceConfirmationService(
    IDeviceConfirmationRepository repository,
    ITrustedContactRepository contacts,
    TrustedContactService trusted,
    IOptionsMonitor<TrustedContactOptions> options,
    UserAccountService accounts,
    ITelegramLinkRepository links,
    IOptionsMonitor<TelegramBotOptions> bot,
    TelegramBotMessages messages,
    IServiceProvider services,
    OtpOptions otpOptions,
    TimeProvider time,
    ILogger<DeviceConfirmationService> logger)
{
    /// <summary>The action of the notice's Approve button (<see cref="DeviceConfirmationDecisionHandler"/>).</summary>
    public const string ApproveAction = "device-confirmations.approve";

    /// <summary>The action of the notice's Deny button (<see cref="DeviceConfirmationDecisionHandler"/>).</summary>
    public const string DenyAction = "device-confirmations.deny";

    /// <summary>How long a sign-in waits for a press.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Whether new devices are checked: trusted contacts run and <c>Identity:TrustedContacts:IsNewDeviceConfirmationRequired</c> is on.</summary>
    public bool IsEnabled => trusted.IsEnabled && options.CurrentValue.IsNewDeviceConfirmationRequired;

    /// <summary>Whether a contact would have to answer for <paramref name="deviceValue"/> before <paramref name="user"/> signs in on it.</summary>
    /// <param name="user">The account.</param>
    /// <param name="deviceValue">The browser's device value (<see cref="DeviceCookie.Ensure"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsConfirmationNeededAsync(IdentityUser user, string deviceValue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return IsEnabled
            && (await trusted.ListDeliverableContactsAsync(user, cancellationToken)).Count > 0
            && !await repository.IsKnownAsync(user.Id, DeviceCookie.HashOf(deviceValue), cancellationToken);
    }

    /// <summary>
    /// Decides what <paramref name="user"/>'s checked code may do on <paramref name="deviceValue"/>: sign in, or wait after a notice to
    /// each deliverable contact. When every chat refuses the notice, nobody could answer, and the sign-in is refused for now.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="deviceValue">The browser's device value (<see cref="DeviceCookie.Ensure"/>).</param>
    /// <param name="ipMasked">The request's masked network, for the notice.</param>
    /// <param name="userAgent">The request's user agent, for the notice.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DeviceCheck> CheckAsync(IdentityUser user, string deviceValue, string? ipMasked, string? userAgent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!IsEnabled)
            return DeviceCheck.Passed;

        var asked = await trusted.ListDeliverableContactsAsync(user, cancellationToken);
        var deviceHash = DeviceCookie.HashOf(deviceValue);
        if (asked.Count == 0 || await repository.IsKnownAsync(user.Id, deviceHash, cancellationToken))
            return DeviceCheck.Passed;

        var now = time.GetUtcNow();
        var token = DeviceCookie.NewValue();
        var confirmation = new IdentityDeviceConfirmation
        {
            Id = Guid.CreateVersion7(now),
            UserId = user.Id,
            DeviceHash = deviceHash,
            TokenHash = DeviceCookie.HashOf(token),
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        await repository.ReplaceAsync(confirmation, cancellationToken);

        var reached = new List<string>();
        foreach (var contact in asked)
        {
            if (await NotifyAsync(contact, user, confirmation, ipMasked, userAgent, cancellationToken))
                reached.Add(TrustedContactService.LabelOf(contact));
        }

        if (reached.Count > 0)
            return new DeviceCheck { Status = DeviceCheckStatus.Waiting, Token = token, ExpiresAt = confirmation.ExpiresAt, Contacts = reached };

        await repository.TryRemoveAsync(confirmation.Id, cancellationToken);
        return DeviceCheck.Unavailable;
    }

    /// <summary>
    /// Reads where a waiting sign-in stands. An approved one is spent here and its browser becomes known, so exactly one poll gets
    /// <see cref="DeviceConfirmationState.Approved"/> and must sign in; a denied one is spent too, so it is logged once.
    /// </summary>
    /// <param name="token">The token <see cref="CheckAsync"/> gave.</param>
    /// <param name="isCookieClient">Whether the client keeps the device cookie; a bearer client's value is never remembered.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DeviceConfirmationPoll> PollAsync(string? token, bool isCookieClient, CancellationToken cancellationToken = default)
    {
        var gone = new DeviceConfirmationPoll { State = DeviceConfirmationState.Gone };
        if (!IsEnabled || string.IsNullOrWhiteSpace(token))
            return gone;

        var now = time.GetUtcNow();
        var confirmation = await repository.FindByTokenAsync(DeviceCookie.HashOf(token), cancellationToken);
        if (confirmation is null || confirmation.ExpiresAt <= now)
            return gone;
        if (confirmation.DecidedAt is null)
            return new DeviceConfirmationPoll { State = DeviceConfirmationState.Pending };
        if (!await repository.TryRemoveAsync(confirmation.Id, cancellationToken)
            || await accounts.FindByIdAsync(confirmation.UserId, cancellationToken) is not { } user)
            return gone;
        if (confirmation.IsApproved != true)
            return new DeviceConfirmationPoll { State = DeviceConfirmationState.Denied, User = user };

        if (isCookieClient)
        {
            await repository.RememberAsync(
                new IdentityKnownDevice { UserId = user.Id, DeviceHash = confirmation.DeviceHash, ConfirmedByUserId = confirmation.DecidedByUserId!.Value, CreatedAt = now },
                cancellationToken);
        }

        return new DeviceConfirmationPoll { State = DeviceConfirmationState.Approved, User = user };
    }

    /// <summary>
    /// Records a contact's press. It changes nothing when the request ended or was answered, or the presser is no longer a contact.
    /// </summary>
    /// <param name="id">The request the button names.</param>
    /// <param name="contactUserId">The contact the button is for.</param>
    /// <param name="isApproved">Whether they approved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DeviceDecision> DecideAsync(Guid id, Guid contactUserId, bool isApproved, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var confirmation = await repository.FindAsync(id, cancellationToken);
        if (!IsEnabled
            || confirmation is null
            || confirmation.DecidedAt is not null
            || confirmation.ExpiresAt <= now
            || await contacts.FindAsync(confirmation.UserId, contactUserId, cancellationToken) is null
            || !await repository.TryDecideAsync(id, contactUserId, isApproved, now, cancellationToken))
        {
            return DeviceDecision.NoLongerOpen;
        }

        return isApproved ? DeviceDecision.Approved : DeviceDecision.Denied;
    }

    /// <summary>Makes the browser known after a sign-in or sign-up through a contact, who answered for it by giving the code.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="contactUserId">The contact.</param>
    /// <param name="deviceValue">The browser's device value (<see cref="DeviceCookie.Ensure"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RememberThroughContactAsync(Guid userId, Guid contactUserId, string deviceValue, CancellationToken cancellationToken = default)
        => repository.RememberAsync(
            new IdentityKnownDevice { UserId = userId, DeviceHash = DeviceCookie.HashOf(deviceValue), ConfirmedByUserId = contactUserId, CreatedAt = time.GetUtcNow() },
            cancellationToken);

    private async Task<bool> NotifyAsync(IdentityUser contact, IdentityUser account, IdentityDeviceConfirmation confirmation, string? ipMasked, string? userAgent, CancellationToken cancellationToken)
    {
        var phone = PhoneNumberPolicy.Normalize(contact.PhoneNumber);
        if (phone is null || await links.FindLinkAsync(bot.CurrentValue.NormalizedBotUserName, phone, cancellationToken) is not { } link)
            return false;

        var culture = messages.CultureOf(null);
        var unknown = messages.Text(TelegramBotMessageKeyConstants.UnknownDevice, culture);
        var text = messages.Format(TelegramBotMessageKeyConstants.DeviceConfirmationRequest, culture, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["account"] = TrustedContactService.AccountNameOf(account),
            ["app"] = otpOptions.Messages.AppName,
            ["device"] = DeviceLabel.Of(userAgent) ?? unknown,
            ["network"] = ipMasked ?? unknown,
            ["time"] = confirmation.CreatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
        });
        var payload = DeviceConfirmationDecisionHandler.PayloadOf(confirmation.Id, contact.Id);
        var sent = await services.GetRequiredService<ITelegramNoticeSender>().SendAsync(
            new TelegramNotice
            {
                Kind = TelegramMessageKindConstants.DeviceConfirmation,
                UserId = account.Id,
                ChatId = link.ChatId,
                Text = text,
                Culture = culture.Name,
                Buttons =
                [
                    new TelegramNoticeButton { Label = messages.Text(TelegramBotMessageKeyConstants.ApproveDeviceButton, culture), Action = ApproveAction, Payload = payload, AllowedTelegramUserId = link.TelegramUserId },
                    new TelegramNoticeButton { Label = messages.Text(TelegramBotMessageKeyConstants.DenyDeviceButton, culture), Action = DenyAction, Payload = payload, AllowedTelegramUserId = link.TelegramUserId },
                ],
            },
            cancellationToken);
        if (!sent.IsSent)
            LogNoticeRefused(logger, sent.FailureReason);
        return sent.IsSent;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The bot's new device notice to a trusted contact was not sent: {Reason}")]
    private static partial void LogNoticeRefused(ILogger logger, string? reason);
}

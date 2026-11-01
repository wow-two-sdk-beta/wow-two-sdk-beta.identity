using System.Globalization;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Channels;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SecurityStamps;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Models;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>
/// Provides the password change the instance's setting asks for (<see cref="PasswordChangeOptions.Confirmation"/>): none,
/// a one-time code or a one-time link, sent to the person's verified channel. It also decides when a sign-in must force a
/// change, because the stored password breaks the current rules, and ends the person's other sessions after every change.
/// </summary>
/// <remarks>
///   - a code or link is an <see cref="IOtpService"/> code, scope <see cref="OtpScope"/>, one use, <see cref="PasswordChangeOptions.Lifetime"/>
///   - delivery names no channel: it takes the first <see cref="VerifiedChannel"/> that <see cref="IUserVerifiedChannelSource"/>
///     lists and <see cref="IOtpDeliveryHandlerFactory"/> can deliver to
///   - a link carries <c>{userId}.{code}</c>, so the page that spends it needs nothing else; the code is the secret
///   - every change rotates the security stamp (the password slice does), which ends every session; this service revokes the
///     others at once, and rebinds the session the change was made in unless <see cref="PasswordChangeOptions.IsSignInAgainRequired"/>
/// </remarks>
/// <param name="passwords">The password slice.</param>
/// <param name="accounts">The core account service.</param>
/// <param name="tokens">The purpose-token issuer, for the forced-change ticket.</param>
/// <param name="otp">Creates and verifies the codes.</param>
/// <param name="handlers">Creates the delivery handler per channel.</param>
/// <param name="formatter">Words a code for its channel and the recipient's culture.</param>
/// <param name="channelSources">The slices that verify a way to reach a person.</param>
/// <param name="identity">Identity options carrying the change settings.</param>
/// <param name="otpOptions">The code's characters and length.</param>
/// <param name="logger">Receives a refused delivery.</param>
/// <param name="sessions">The sessions slice; null leaves session handling to the stamp rotation.</param>
/// <param name="stamps">The stamp validator, whose cached stamp a change drops; null without the slice.</param>
public sealed class UserPasswordChangeService(
    UserPasswordService passwords,
    UserAccountService accounts,
    UserTokenIssuer tokens,
    IOtpService otp,
    IOtpDeliveryHandlerFactory handlers,
    IOtpMessageFormatter formatter,
    IEnumerable<IUserVerifiedChannelSource> channelSources,
    IdentityCoreOptions identity,
    OtpOptions otpOptions,
    ILogger<UserPasswordChangeService> logger,
    UserSessionService? sessions = null,
    SecurityStampValidator? stamps = null)
{
    /// <summary>The scope of a change's code or link in <see cref="IOtpService"/>.</summary>
    public const string OtpScope = "password-change";

    /// <summary>The purpose a delivered code is worded for, as <see cref="IOtpMessageFormatter"/> templates key it.</summary>
    public const string MessagePurpose = "password-change";

    /// <summary>The characters in a link's secret; longer than a typed code, because no one types it.</summary>
    private const int LinkSecretLength = 12;

    private PasswordChangeOptions Change => identity.Password.Change;

    /// <summary>How this instance confirms a change.</summary>
    public PasswordChangeConfirmationType Confirmation => Change.Confirmation;

    /// <summary>Whether a change also ends the session it was made in.</summary>
    public bool IsSignInAgainRequired => Change.IsSignInAgainRequired;

    /// <summary>
    /// Whether a password that just matched its hash breaks the current rules and so must change before the person signs in.
    /// Only the structured rules count; the breach check, which needs a network, does not run at sign-in.
    /// </summary>
    /// <param name="user">The person.</param>
    /// <param name="typedPassword">The password as typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsChangeRequiredAsync(IdentityUser user, string typedPassword, CancellationToken cancellationToken = default)
        => !(await passwords.ValidateRulesAsync(user, typedPassword, cancellationToken)).IsSuccess;

    /// <summary>
    /// Issue the ticket that lets a person whose password breaks the current rules reach the change routes, and nothing
    /// else: a purpose token bound to the person and their security stamp, so the change that follows spends it.
    /// </summary>
    /// <param name="user">The person whose password matched.</param>
    public string IssueRequiredChangeTicket(IdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return PasswordChangeTokens.Compose(user.Id, tokens.Issue(user, UserTokenPurposeConstants.PasswordChangeRequired));
    }

    /// <summary>The person a live forced-change ticket names; null when the ticket is malformed, expired, or spent by a change.</summary>
    /// <param name="ticket">The ticket the sign-in answered with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityUser?> RedeemRequiredChangeTicketAsync(string? ticket, CancellationToken cancellationToken = default)
    {
        if (!PasswordChangeTokens.TryParse(ticket, out var userId, out var token))
            return null;

        var user = await accounts.FindByIdAsync(userId, cancellationToken);
        return user is not null && tokens.Verify(user, UserTokenPurposeConstants.PasswordChangeRequired, token) ? user : null;
    }

    /// <summary>
    /// Ask for the change's confirmation: under <see cref="PasswordChangeConfirmationType.Code"/> or
    /// <see cref="PasswordChangeConfirmationType.Link"/> send a one-time code or link to the person's verified channel; under
    /// <see cref="PasswordChangeConfirmationType.None"/> send nothing.
    /// </summary>
    /// <param name="user">The person changing their password.</param>
    /// <param name="culture">The recipient's culture for the wording; null takes the current UI culture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PasswordChangeConfirmationResult> SendConfirmationAsync(IdentityUser user, CultureInfo? culture = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var confirmation = Change.Confirmation;
        if (confirmation == PasswordChangeConfirmationType.None)
            return new PasswordChangeConfirmationResult { Confirmation = confirmation, Status = PasswordChangeConfirmationStatus.NotRequired };

        var reachable = new List<(VerifiedChannel Channel, IOtpDeliveryHandler Handler)>();
        foreach (var source in channelSources)
        {
            foreach (var channel in await source.ListAsync(user, cancellationToken))
            {
                if (handlers.Create(channel.Channel) is { } handler)
                    reachable.Add((channel, handler));
            }
        }

        if (reachable.Count == 0)
            return new PasswordChangeConfirmationResult { Confirmation = confirmation, Status = PasswordChangeConfirmationStatus.NoVerifiedChannel };

        var isLink = confirmation == PasswordChangeConfirmationType.Link;
        var spec = isLink
            ? new OtpCodeSpec { Kind = OtpCodeKind.Alphanumeric, Length = LinkSecretLength, Lifetime = Change.Lifetime }
            : new OtpCodeSpec { Kind = otpOptions.CodeKind, Length = otpOptions.CodeLength, Lifetime = Change.Lifetime };
        var created = await otp.CreateAsync(SubjectOf(user), OtpScope, spec, cancellationToken);
        if (!created.IsSuccess)
            return new PasswordChangeConfirmationResult { Confirmation = confirmation, Status = PasswordChangeConfirmationStatus.RateLimited };

        culture ??= CultureInfo.CurrentUICulture;
        string? failure = null;
        foreach (var (channel, handler) in reachable)
        {
            var delivered = await handler.SendAsync(
                isLink ? LinkEnvelope(user, channel, created.Code!, culture) : CodeEnvelope(channel, created.Code!, culture),
                cancellationToken);
            if (delivered.IsSuccess)
                return new PasswordChangeConfirmationResult { Confirmation = confirmation, Status = PasswordChangeConfirmationStatus.Sent, ExpiresAt = created.ExpiresAt };

            failure = delivered.FailureReason;
            logger.PasswordChangeDeliveryFailed(channel.Channel, delivered.FailureReason);
        }

        return new PasswordChangeConfirmationResult
        {
            Confirmation = confirmation,
            Status = PasswordChangeConfirmationStatus.DeliveryFailed,
            FailureReason = failure,
        };
    }

    /// <summary>
    /// Change the password of a person who is signed in, or holds a forced-change ticket, under <c>None</c> or <c>Code</c>.
    /// Under <c>Link</c> only <see cref="ChangeByLinkAsync"/> changes a password. Ends the person's other sessions.
    /// </summary>
    /// <param name="user">The person.</param>
    /// <param name="currentPassword">The password they claim is current; unused when <paramref name="isTicketed"/>.</param>
    /// <param name="newPassword">The replacement password.</param>
    /// <param name="code">The code that was sent, under <c>Code</c>.</param>
    /// <param name="isTicketed">Whether a forced-change ticket stands in for the current password: the sign-in just verified it.</param>
    /// <param name="currentSessionId">The session the change is made in, which stays signed in unless <see cref="PasswordChangeOptions.IsSignInAgainRequired"/>; null for none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PasswordChangeResult> ChangeAsync(
        IdentityUser user,
        string? currentPassword,
        string newPassword,
        string? code,
        bool isTicketed,
        Guid? currentSessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(newPassword);

        if (Change.Confirmation == PasswordChangeConfirmationType.Link)
            return PasswordChangeResult.Failed(PasswordChangeErrorCodeConstants.LinkRequired, "Change your password with the link sent to you.");

        if (!isTicketed && (string.IsNullOrEmpty(currentPassword) || !await passwords.CheckPasswordAsync(user, currentPassword, cancellationToken)))
            return PasswordChangeResult.Failed(IdentityErrorCodeConstants.PasswordMismatch, "The current password is incorrect.");

        // The rules run before the code is spent, so a weak new password leaves the code usable for the next try.
        var rules = await passwords.ValidateAsync(user, newPassword, cancellationToken);
        if (!rules.IsSuccess)
            return PasswordChangeResult.Failed(rules);

        if (Change.Confirmation == PasswordChangeConfirmationType.Code
            && (string.IsNullOrWhiteSpace(code) || !(await otp.VerifyAsync(SubjectOf(user), code, OtpScope, cancellationToken)).IsSuccess))
            return PasswordChangeResult.Failed(PasswordChangeErrorCodeConstants.InvalidCode, "The code is invalid or has expired.");

        return await StoreAsync(user, newPassword, currentSessionId, cancellationToken);
    }

    /// <summary>
    /// Change the password of the person a link names, under <c>Link</c>: the link proved they reach their verified channel,
    /// so no current password is asked. Ends every session of the person.
    /// </summary>
    /// <param name="token">The link's token, <c>{userId}.{code}</c>.</param>
    /// <param name="newPassword">The replacement password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PasswordChangeResult> ChangeByLinkAsync(string? token, string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newPassword);

        var invalid = PasswordChangeResult.Failed(PasswordChangeErrorCodeConstants.InvalidLink, "The link is invalid or has expired.");
        if (Change.Confirmation != PasswordChangeConfirmationType.Link || !PasswordChangeTokens.TryParse(token, out var userId, out var code))
            return invalid;

        var user = await accounts.FindByIdAsync(userId, cancellationToken);
        if (user is null)
            return invalid;

        var rules = await passwords.ValidateAsync(user, newPassword, cancellationToken);
        if (!rules.IsSuccess)
            return PasswordChangeResult.Failed(rules);

        return (await otp.VerifyAsync(SubjectOf(user), code, OtpScope, cancellationToken)).IsSuccess
            ? await StoreAsync(user, newPassword, currentSessionId: null, cancellationToken)
            : invalid;
    }

    private async Task<PasswordChangeResult> StoreAsync(IdentityUser user, string newPassword, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        var stored = await passwords.ReplaceValidatedAsync(user, newPassword, cancellationToken);
        if (!stored.IsSuccess)
            return PasswordChangeResult.Failed(stored);

        // The rotated stamp ended every session; ending them here too reaches the grants issued under each one at once.
        stamps?.Forget(user.Id);
        var isSessionKept = false;
        if (sessions is not null)
        {
            var keep = Change.IsSignInAgainRequired ? null : currentSessionId;
            await sessions.RevokeAllAsync(user, keep, cancellationToken);
            if (keep is { } sessionId)
            {
                await sessions.RebindAsync(user, sessionId, cancellationToken);
                isSessionKept = true;
            }
        }

        return new PasswordChangeResult { IsSuccess = true, User = user, IsSessionKept = isSessionKept };
    }

    private OtpDeliveryEnvelopeModel CodeEnvelope(VerifiedChannel channel, string code, CultureInfo culture)
    {
        var worded = formatter.Format(MessagePurpose, channel.Channel, code, Change.Lifetime, culture);
        return new OtpDeliveryEnvelopeModel
        {
            DeliveryAddress = channel.Address,
            Code = code,
            Scope = MessagePurpose,
            Text = worded.Text,
            Subject = worded.Subject,
            Lifetime = Change.Lifetime,
            Culture = culture.Name,
        };
    }

    private OtpDeliveryEnvelopeModel LinkEnvelope(IdentityUser user, VerifiedChannel channel, string code, CultureInfo culture)
    {
        var token = PasswordChangeTokens.Compose(user.Id, code);
        var link = Change.LinkTemplate!.Replace(PasswordChangeOptions.TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal);
        var minutes = ((int)Math.Ceiling(Change.Lifetime.TotalMinutes)).ToString(CultureInfo.InvariantCulture);
        return new OtpDeliveryEnvelopeModel
        {
            DeliveryAddress = channel.Address,
            Code = token,
            Scope = MessagePurpose,
            Text = Change.LinkBody.Replace("{link}", link, StringComparison.Ordinal).Replace("{minutes}", minutes, StringComparison.Ordinal),
            Subject = Change.LinkSubject,
            Lifetime = Change.Lifetime,
            Culture = culture.Name,
        };
    }

    private static string SubjectOf(IdentityUser user) => user.Id.ToString();
}

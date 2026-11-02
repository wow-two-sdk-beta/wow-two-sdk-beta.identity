using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Models;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>
/// Provides sign-in by phone with a code the instance's Telegram bot sends. A number linked to a chat gets its code at
/// once; any other gets a start link, and the bot sends the code once the person shares that number as their own
/// contact. Whether an account holds the number changes neither answer, but the two answers differ, so a send tells a
/// number linked to the bot from one that is not. A verified code signs in the person holding the number confirmed, or
/// creates them while sign-up is on. Numbers outside <c>Identity:Phone:AllowedCountries</c> are refused, and sending is
/// rate-limited per number by <c>Identity:Otp:RateLimitWindow</c> and per origin by <c>Identity:Otp:OriginRateLimitWindow</c>.
/// </summary>
/// <remarks>
///   - a code that could not be delivered is discarded, so nothing the person never received waits to be guessed
///   - a locked-out person's code is refused before it is checked, so it stays unspent for after the lockout
///   - wrong codes share the number's failure budget (<c>Identity:Otp:MaxSubjectFailures</c>) across every code sent
/// </remarks>
/// <param name="users">Finds the person holding a confirmed number.</param>
/// <param name="accounts">Creates a person on the first sign-in.</param>
/// <param name="otp">Creates and spends the codes.</param>
/// <param name="channels">The <c>telegram</c> delivery channel.</param>
/// <param name="formatter">Words the code message.</param>
/// <param name="otpOptions">The code's shape and lifetime.</param>
/// <param name="phones">The number rules and the allowed countries.</param>
/// <param name="telegram">The number's chat and start links.</param>
/// <param name="lockout">The lockout slice; null skips the check before the code is spent.</param>
public sealed class UserPhoneSignInService(
    IUserRepository users,
    UserAccountService accounts,
    IOtpService otp,
    IOtpDeliveryHandlerFactory channels,
    IOtpMessageFormatter formatter,
    OtpOptions otpOptions,
    PhoneNumberPolicy phones,
    TelegramLinkService telegram,
    UserLockoutService? lockout = null)
{
    /// <summary>The OTP scope of phone sign-in codes.</summary>
    public const string CodeScope = UserPhoneService.SignInScope;

    /// <summary>The purpose the code message is worded for (<c>Identity:Otp:Messages</c> keys).</summary>
    public const string MessagePurpose = "phone-sign-in";

    /// <summary>Whether phone sign-in runs: the instance's bot is on.</summary>
    public bool IsEnabled => telegram.IsEnabled;

    /// <summary>
    /// Sends a sign-in code for <paramref name="phoneNumber"/> to its linked chat, or makes a start link when it has
    /// none or the chat refused the message.
    /// </summary>
    /// <param name="phoneNumber">The number as typed.</param>
    /// <param name="origin">Where the request came from, such as the client address; null limits by number only.</param>
    /// <param name="culture">The person's culture for the code message; null takes the current UI culture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PhoneSignInResult> SendAsync(string? phoneNumber, string? origin, CultureInfo? culture = null, CancellationToken cancellationToken = default)
    {
        var phone = PhoneNumberPolicy.Normalize(phoneNumber);
        if (phone is null)
            return PhoneSignInResult.Of(PhoneSignInStatus.InvalidNumber);
        if (!phones.IsAllowed(phone))
            return PhoneSignInResult.Of(PhoneSignInStatus.CountryNotAllowed, phone);
        if (await otp.IsRateLimitedAsync(phone, CodeScope, origin, cancellationToken)
            || await telegram.IsRateLimitedAsync(phone, origin, cancellationToken))
        {
            return PhoneSignInResult.Of(PhoneSignInStatus.RateLimited, phone);
        }

        if (await telegram.FindChatAsync(phone, cancellationToken) is { } chatId)
        {
            var sent = await SendCodeAsync(phone, chatId, origin, culture ?? CultureInfo.CurrentUICulture, isKeyboardRemoved: false, cancellationToken);
            if (sent.Status is PhoneSignInStatus.CodeSent or PhoneSignInStatus.RateLimited)
                return sent;
        }

        var url = await telegram.CreateStartLinkAsync(phone, TelegramLinkPurposeConstants.PhoneSignIn, origin, cancellationToken);
        return new PhoneSignInResult { Status = PhoneSignInStatus.LinkRequired, PhoneNumber = phone, TelegramLinkUrl = url };
    }

    /// <summary>
    /// Creates a sign-in code for <paramref name="phoneNumber"/> and sends it to <paramref name="chatId"/>:
    /// <see cref="PhoneSignInStatus.CodeSent"/>, <see cref="PhoneSignInStatus.RateLimited"/>, or
    /// <see cref="PhoneSignInStatus.LinkRequired"/> when the chat refused it, and the code is discarded.
    /// </summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="chatId">The number's linked chat.</param>
    /// <param name="origin">Where the request came from; null limits by number only.</param>
    /// <param name="culture">The person's culture for the code message.</param>
    /// <param name="isKeyboardRemoved">Whether the message also removes the bot's share-contact button.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PhoneSignInResult> SendCodeAsync(string phoneNumber, long chatId, string? origin, CultureInfo culture, bool isKeyboardRemoved, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentNullException.ThrowIfNull(culture);

        var created = await otp.CreateAsync(phoneNumber, CodeScope, otpOptions.DefaultSpec, origin, cancellationToken);
        if (!created.IsSuccess)
            return PhoneSignInResult.Of(PhoneSignInStatus.RateLimited, phoneNumber);

        var channel = channels.Create(OtpChannelNameConstants.Telegram)
            ?? throw new InvalidOperationException("Phone sign-in needs the telegram OTP channel; call .AddTelegramLinks().");
        var message = formatter.Format(MessagePurpose, OtpChannelNameConstants.Telegram, created.Code!, otpOptions.CodeLifetime, culture);
        var delivered = await channel.SendAsync(
            new OtpDeliveryEnvelopeModel
            {
                DeliveryAddress = chatId.ToString(CultureInfo.InvariantCulture),
                Code = created.Code!,
                Scope = CodeScope,
                Text = message.Text,
                Subject = message.Subject,
                Lifetime = otpOptions.CodeLifetime,
                Culture = culture.Name,
                Metadata = isKeyboardRemoved
                    ? new Dictionary<string, string>(StringComparer.Ordinal) { [TelegramOtpMetadataConstants.RemoveKeyboard] = "true" }
                    : null,
            },
            cancellationToken);

        if (delivered.IsSuccess)
            return PhoneSignInResult.Of(PhoneSignInStatus.CodeSent, phoneNumber);

        // The chat refused it (the bot was blocked): no code the person never saw stays open to guessing.
        if (created.CodeId is { } codeId)
            await otp.DiscardAsync(codeId, cancellationToken);
        return PhoneSignInResult.Of(PhoneSignInStatus.LinkRequired, phoneNumber);
    }

    /// <summary>
    /// Spends <paramref name="code"/> for <paramref name="phoneNumber"/> and returns the person holding the number
    /// confirmed; with none, creates one holding it confirmed while <paramref name="isSignUpAllowed"/>, else
    /// <see cref="PhoneSignInStatus.SignUpClosed"/>. A locked-out person answers <see cref="PhoneSignInStatus.LockedOut"/>
    /// before the code is checked, so it is not spent.
    /// </summary>
    /// <param name="phoneNumber">The number as typed.</param>
    /// <param name="code">The code the bot sent.</param>
    /// <param name="isSignUpAllowed">Whether a first sign-in may create the person: sign-up is open, or an invitation came with it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PhoneSignInResult> VerifyAsync(string? phoneNumber, string? code, bool isSignUpAllowed, CancellationToken cancellationToken = default)
    {
        var phone = PhoneNumberPolicy.Normalize(phoneNumber);
        if (phone is null)
            return PhoneSignInResult.Of(PhoneSignInStatus.InvalidNumber);
        if (!phones.IsAllowed(phone))
            return PhoneSignInResult.Of(PhoneSignInStatus.CountryNotAllowed, phone);

        var known = await users.FindByConfirmedPhoneNumberAsync(phone, cancellationToken);
        if (known is not null && lockout?.IsLockedOut(known) == true)
            return new PhoneSignInResult { Status = PhoneSignInStatus.LockedOut, PhoneNumber = phone, User = known };
        if (string.IsNullOrWhiteSpace(code) || !(await otp.VerifyAsync(phone, code.Trim(), CodeScope, cancellationToken)).IsSuccess)
            return PhoneSignInResult.Of(PhoneSignInStatus.InvalidCode, phone);

        if (known is not null)
            return new PhoneSignInResult { Status = PhoneSignInStatus.Verified, PhoneNumber = phone, User = known };
        if (!isSignUpAllowed)
            return PhoneSignInResult.Of(PhoneSignInStatus.SignUpClosed, phone);

        var created = await CreateAsync(phone, phone, cancellationToken)
            ?? await CreateAsync(phone, "phone-" + Guid.NewGuid().ToString("N"), cancellationToken)
            ?? throw new InvalidOperationException("The first phone sign-in could not create the person.");
        return new PhoneSignInResult { Status = PhoneSignInStatus.Verified, PhoneNumber = phone, User = created, IsUserCreated = true };
    }

    /// <summary>Creates a person holding <paramref name="phone"/> confirmed under <paramref name="userName"/>; null when the name is taken.</summary>
    private async Task<IdentityUser?> CreateAsync(string phone, string userName, CancellationToken cancellationToken)
    {
        var user = new IdentityUser { UserName = userName, PhoneNumber = phone, IsPhoneNumberConfirmed = true };
        var result = await accounts.CreateAsync(user, cancellationToken);
        if (result.IsSuccess)
            return user;

        return result.Errors.Any(error => error.Code == IdentityErrorCodeConstants.DuplicateUserName)
            ? null
            : throw new InvalidOperationException(result.Errors[0].Description);
    }
}

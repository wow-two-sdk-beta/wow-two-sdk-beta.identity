using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>
/// Provides sign-up from an invitation with the phone the instance's bot verifies, no code typed: the browser gives the
/// profile the sign-up asks for (<c>Identity:SignUp:Profile</c>) and gets a start link; the person shares their own contact
/// in the bot, which proves the number and links the chat; the person, new or known by that number, joins the invitation's
/// organization with its role; the browser that started it polls and is signed in once.
/// </summary>
/// <remarks>
///   - only the browser holding the secret reads the sign-up; only the secret's SHA-256 is stored
///   - a picture is checked and re-encoded before anything is stored (<see cref="IProfilePictureProcessor"/>), kept with the
///     sign-up until the join, then moved to <see cref="IProfilePictureStore"/>; a finished sign-up drops it
///   - an invitation naming a phone binds the start link to it; any other takes the contact's number, which must be in
///     <c>Identity:Phone:AllowedCountries</c>
///   - a known person keeps their name and picture when they have one; the join and the new person are one transaction
/// </remarks>
/// <param name="invitations">Finds and redeems the invitation.</param>
/// <param name="requests">The waiting sign-ups.</param>
/// <param name="telegram">Makes the start link.</param>
/// <param name="signUp">The profile fields the sign-up asks for.</param>
/// <param name="pictureRules">The picture rules.</param>
/// <param name="processor">Checks and re-encodes the picture.</param>
/// <param name="pictures">Keeps the joined person's picture.</param>
/// <param name="users">Finds the person holding a confirmed number.</param>
/// <param name="accounts">Creates and updates people.</param>
/// <param name="phones">The allowed countries.</param>
/// <param name="time">The clock sign-ups are stamped with.</param>
public sealed class InviteSignUpService(
    InvitationService invitations,
    ISignUpRequestRepository requests,
    TelegramLinkService telegram,
    IOptionsMonitor<SignUpOptions> signUp,
    IOptionsMonitor<ProfilePictureOptions> pictureRules,
    IProfilePictureProcessor processor,
    IProfilePictureStore pictures,
    IUserRepository users,
    UserAccountService accounts,
    PhoneNumberPolicy phones,
    TimeProvider time)
{
    private const int SecretBytes = 32;

    /// <summary>Starts a sign-up for the invitation <paramref name="request"/> names; see <see cref="InviteSignUpService"/>.</summary>
    /// <param name="request">The invitation's token and the profile given.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InviteSignUpStartResult> StartAsync(InviteSignUpStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!telegram.IsEnabled)
            return InviteSignUpStartResult.Of(InviteSignUpStartStatus.Unavailable);

        var lookup = await invitations.FindAsync(request.Token, cancellationToken);
        if (lookup is null)
            return InviteSignUpStartResult.Of(InviteSignUpStartStatus.InvitationRefused, InvitationService.NotFound());
        if (lookup.Status != InvitationStatus.Pending)
            return InviteSignUpStartResult.Of(InviteSignUpStartStatus.InvitationRefused, InvitationService.RefusalOf(lookup.Status));

        var profile = signUp.CurrentValue.Profile;
        if (CheckName(request.DisplayName, profile.DisplayName, out var name) is { } nameRefusal)
            return InviteSignUpStartResult.Of(InviteSignUpStartStatus.Invalid, nameRefusal);
        if (CheckPicture(request.Picture, profile.Picture, out var picture) is { } pictureRefusal)
            return InviteSignUpStartResult.Of(InviteSignUpStartStatus.Invalid, pictureRefusal);

        var phone = lookup.Invitation.PhoneNumber;
        if (await telegram.IsRateLimitedAsync(phone, request.Origin, cancellationToken))
            return InviteSignUpStartResult.Of(InviteSignUpStartStatus.RateLimited);

        var now = time.GetUtcNow();
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
        var signUpRequest = new IdentitySignUpRequest
        {
            Id = Guid.CreateVersion7(now),
            InvitationId = lookup.Invitation.Id,
            DisplayName = name,
            Picture = picture,
            BrowserSecretHash = HashOf(secret),
            ClientId = ClientIdOf(request.ClientId),
            CreatedAt = now,
            ExpiresAt = Min(now + telegram.LinkLifetime, lookup.Invitation.ExpiresAt),
        };
        await requests.AddAsync(signUpRequest, cancellationToken);
        var url = await telegram.CreateStartLinkAsync(phone, TelegramLinkPurposeConstants.InviteSignUp, signUpRequest.Id, request.Origin, cancellationToken);
        return new InviteSignUpStartResult
        {
            Status = InviteSignUpStartStatus.Started,
            VerificationId = signUpRequest.Id,
            BrowserSecret = secret,
            TelegramLinkUrl = url,
            ExpiresAt = signUpRequest.ExpiresAt,
        };
    }

    /// <summary>
    /// Finishes the sign-up a shared contact just proved <paramref name="phoneNumber"/> for: the person, new or known, joins
    /// the invitation's organization; the bot's reply key comes back.
    /// </summary>
    /// <param name="signUpId">The sign-up the start link was made for.</param>
    /// <param name="phoneNumber">The proven number in E.164.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text the bot answers with (<see cref="TelegramBotMessageKeyConstants"/>).</returns>
    public async Task<string> CompleteAsync(Guid signUpId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        var now = time.GetUtcNow();
        var request = await requests.FindAsync(signUpId, cancellationToken);
        if (request is null || request.CompletedAt is not null || request.ExpiresAt <= now)
            return TelegramBotMessageKeyConstants.SignUpFailed;

        if (!phones.IsAllowed(phoneNumber))
            return await FailAsync(request, IdentityErrorCodeConstants.PhoneCountryNotAllowed, TelegramBotMessageKeyConstants.CountryNotAllowed, cancellationToken);

        var lookup = await invitations.FindByIdAsync(request.InvitationId, cancellationToken);
        if (lookup is null)
            return await FailAsync(request, IdentityErrorCodeConstants.InvitationNotFound, TelegramBotMessageKeyConstants.SignUpFailed, cancellationToken);

        var (person, redemption) = await invitations.RedeemWithAsync(
            lookup.Invitation,
            token => PersonOfAsync(phoneNumber, request, token),
            joined => joined,
            cancellationToken);
        if (redemption is not { IsSuccess: true } || person is null)
        {
            var code = redemption is { Errors.Count: > 0 } refused ? refused.Errors[0].Code : IdentityErrorCodeConstants.InvitationNotFound;
            var reply = code == IdentityErrorCodeConstants.InvitationIdentifierMismatch
                ? TelegramBotMessageKeyConstants.InvitationMismatch
                : TelegramBotMessageKeyConstants.SignUpFailed;
            return await FailAsync(request, code, reply, cancellationToken);
        }

        await requests.TryCompleteAsync(request.Id, person.Id, failureCode: null, time.GetUtcNow(), cancellationToken);
        return TelegramBotMessageKeyConstants.SignUpDone;
    }

    /// <summary>
    /// Reads the sign-up <paramref name="signUpId"/> for the browser holding <paramref name="browserSecret"/>; the first read
    /// after the join names the person to sign that browser in as.
    /// </summary>
    /// <param name="signUpId">The sign-up.</param>
    /// <param name="browserSecret">The secret the starting browser keeps.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InviteSignUpPoll> PollAsync(Guid signUpId, string? browserSecret, CancellationToken cancellationToken = default)
    {
        var request = await requests.FindAsync(signUpId, cancellationToken);
        if (request is null || string.IsNullOrEmpty(browserSecret)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(request.BrowserSecretHash), Encoding.ASCII.GetBytes(HashOf(browserSecret))))
        {
            return InviteSignUpPoll.Of(InviteSignUpState.NotFound);
        }

        if (request.CompletedAt is null)
            return InviteSignUpPoll.Of(request.ExpiresAt <= time.GetUtcNow() ? InviteSignUpState.Expired : InviteSignUpState.Pending);
        if (request.UserId is not { } userId)
            return InviteSignUpPoll.Of(InviteSignUpState.Failed, request.FailureCode);
        if (!await requests.TryMarkSignedInAsync(request.Id, time.GetUtcNow(), cancellationToken))
            return InviteSignUpPoll.Of(InviteSignUpState.Completed);

        return new InviteSignUpPoll
        {
            State = InviteSignUpState.Completed,
            SignInAs = await accounts.FindByIdAsync(userId, cancellationToken),
            ClientId = request.ClientId,
        };
    }

    /// <summary>The person holding <paramref name="phone"/> confirmed, given the sign-up's name and picture where they lack one, or a new person holding it.</summary>
    private async Task<IdentityUser?> PersonOfAsync(string phone, IdentitySignUpRequest request, CancellationToken cancellationToken)
    {
        var person = await users.FindByConfirmedPhoneNumberAsync(phone, cancellationToken);
        if (person is not null)
        {
            if (string.IsNullOrWhiteSpace(person.DisplayName) && request.DisplayName is { } name)
            {
                person.DisplayName = name;
                await accounts.UpdateAsync(person, cancellationToken);
            }

            if (request.Picture is { } kept && await pictures.FindVersionAsync(person.Id, cancellationToken) is null)
                await pictures.SaveAsync(person.Id, kept, cancellationToken);
            return person;
        }

        person = await CreateAsync(phone, phone, request.DisplayName, cancellationToken)
            ?? await CreateAsync(phone, "phone-" + Guid.NewGuid().ToString("N"), request.DisplayName, cancellationToken)
            ?? throw new InvalidOperationException("The invitation sign-up could not create the person.");
        if (request.Picture is { } picture)
            await pictures.SaveAsync(person.Id, picture, cancellationToken);
        return person;
    }

    /// <summary>Creates a person holding <paramref name="phone"/> confirmed under <paramref name="userName"/>; null when the name is taken.</summary>
    private async Task<IdentityUser?> CreateAsync(string phone, string userName, string? displayName, CancellationToken cancellationToken)
    {
        var user = new IdentityUser { UserName = userName, PhoneNumber = phone, IsPhoneNumberConfirmed = true, DisplayName = displayName };
        var result = await accounts.CreateAsync(user, cancellationToken);
        if (result.IsSuccess)
            return user;

        return result.Errors.Any(error => error.Code == IdentityErrorCodeConstants.DuplicateUserName)
            ? null
            : throw new InvalidOperationException(result.Errors[0].Description);
    }

    private async Task<string> FailAsync(IdentitySignUpRequest request, string failureCode, string reply, CancellationToken cancellationToken)
    {
        await requests.TryCompleteAsync(request.Id, userId: null, failureCode, time.GetUtcNow(), cancellationToken);
        return reply;
    }

    /// <summary>The trimmed name the field's mode lets through, or the refusal.</summary>
    private static IdentityResult? CheckName(string? given, ProfileFieldMode mode, out string? name)
    {
        name = null;
        if (mode == ProfileFieldMode.Off)
            return null;

        var trimmed = given?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return mode == ProfileFieldMode.Required
                ? IdentityUserExtensions.Failure(IdentityErrorCodeConstants.DisplayNameRequired, "Enter the name you go by.")
                : null;
        }

        if (trimmed.Length > IdentityDbContext.DisplayNameMaxLength)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.DisplayNameTooLong, $"The name must be at most {IdentityDbContext.DisplayNameMaxLength} characters.");

        name = trimmed;
        return null;
    }

    /// <summary>The processed picture the field's mode lets through, or the refusal.</summary>
    private IdentityResult? CheckPicture(byte[]? upload, ProfileFieldMode mode, out byte[]? picture)
    {
        picture = null;
        if (mode == ProfileFieldMode.Off)
            return null;
        if (upload is null || upload.Length == 0)
        {
            return mode == ProfileFieldMode.Required
                ? IdentityUserExtensions.Failure(IdentityErrorCodeConstants.PictureRequired, "Add a picture of yourself.")
                : null;
        }

        var rules = pictureRules.CurrentValue;
        var processed = processor.Process(upload, rules);
        if (processed.IsAccepted)
        {
            picture = processed.Content;
            return null;
        }

        return processed.Status switch
        {
            ProfilePictureStatus.TooLarge => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.PictureTooLarge, $"The picture must be at most {rules.MaxUploadBytes / (1024 * 1024)} MB."),
            ProfilePictureStatus.UnsupportedType => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.PictureUnsupportedType, "The picture must be a JPEG, PNG or WebP image."),
            ProfilePictureStatus.TooSmall => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.PictureTooSmall, $"The picture must be at least {rules.MinDimension}×{rules.MinDimension} pixels."),
            ProfilePictureStatus.TooBig => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.PictureTooBig, $"The picture must be at most {rules.MaxDimension}×{rules.MaxDimension} pixels."),
            _ => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.PictureUnreadable, "The picture could not be read; try another one."),
        };
    }

    private static string? ClientIdOf(string? clientId)
    {
        var trimmed = clientId?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, 100)];
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static string HashOf(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

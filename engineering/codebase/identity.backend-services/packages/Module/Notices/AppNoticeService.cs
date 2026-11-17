using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Sends an app's notice to one person through the instance's Telegram bot: the chat their confirmed phone is linked to,
/// once per app and idempotency key, never to a person who muted the app, inside the app's rate limit and Telegram's pace.
/// The notice stays in the chat (<see cref="TelegramNotice.IsKept"/>) with the app's link buttons and a mute button.
/// </summary>
/// <remarks>
///   - order: the body's rules, the bot, the app's rate limit, the key (a repeat answers the first outcome), the person's
///     link, their mute, Telegram's pace, the send
///   - a key whose person has no link, or whose send failed, is given back, so a retry may send; a muted one keeps
///     <see cref="AppNoticeStatusConstants.Muted"/>
///   - a send cut off before its outcome is recorded leaves its key pending; a retry after <see cref="StaleClaim"/> takes it
///     over and sends again
///   - codes and the bot's own security notices never pass here, so a mute never stops them
/// </remarks>
/// <param name="users">Finds the person.</param>
/// <param name="links">Finds the chat their phone is linked to.</param>
/// <param name="sender">Sends and records the notice.</param>
/// <param name="store">The keys and the mutes.</param>
/// <param name="pace">The rate limit and Telegram's pace.</param>
/// <param name="messages">The bot's wording, for the mute button.</param>
/// <param name="bot">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="options">The limits; <c>Identity:Notices</c> reloads live.</param>
/// <param name="time">The clock keys are claimed and pruned by.</param>
public sealed class AppNoticeService(
    IUserRepository users,
    ITelegramLinkRepository links,
    ITelegramNoticeSender sender,
    IAppNoticeRepository store,
    AppNoticePace pace,
    TelegramBotMessages messages,
    IOptionsMonitor<TelegramBotOptions> bot,
    IOptionsMonitor<AppNoticeOptions> options,
    TimeProvider time)
{
    /// <summary>The ledger kind of every app notice.</summary>
    public const string Kind = "app-notice";

    /// <summary>The action of the mute button on each app notice (<see cref="AppNoticeMuteHandler"/>); its payload is the app's client id.</summary>
    public const string MuteAction = "app-notices.mute";

    /// <summary>How long a pending key holds before a retry takes it over.</summary>
    public static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(1);

    private const int MaxCultureLength = 16;

    /// <summary>Sends <paramref name="notice"/> as the app <paramref name="clientId"/>.</summary>
    /// <param name="clientId">The sending app: its token's client id, or the in-process host's name.</param>
    /// <param name="notice">The notice.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The delivery, or a failure coded with <see cref="AppNoticeErrorCodeConstants"/>.</returns>
    public async Task<Result<AppNoticeDelivery>> SendAsync(string clientId, AppNotice notice, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clientId.Length, AppNoticeOptions.MaxClientIdLength, nameof(clientId));
        ArgumentNullException.ThrowIfNull(notice);
        if (FindProblem(notice) is { } problem)
            return Result<AppNoticeDelivery>.Fail(AppNoticeErrors.Invalid(problem));

        // A body read from JSON may carry "buttons": null.
        notice = notice with { Buttons = notice.Buttons ?? [] };

        var current = bot.CurrentValue;
        if (!current.IsEnabled)
            return Result<AppNoticeDelivery>.Fail(AppNoticeErrors.Unavailable("The instance's Telegram bot is off.", TimeSpan.FromMinutes(1)));
        if (!pace.TryAdmit(clientId, out var retryAfter))
            return Result<AppNoticeDelivery>.Fail(AppNoticeErrors.RateLimited(retryAfter));

        var now = time.GetUtcNow();
        var settings = options.CurrentValue;
        var request = new IdentityAppNotice
        {
            Id = Guid.CreateVersion7(now),
            ClientId = clientId,
            IdempotencyKey = notice.IdempotencyKey,
            RequestHash = HashOf(notice),
            UserId = Guid.Parse(notice.Subject),
            CreatedAt = now,
        };
        var claim = await store.ClaimAsync(request, now - StaleClaim, now - settings.IdempotencyWindow, cancellationToken);
        if (claim.ClaimedId is not { } claimedId)
            return Replay(claim.Existing!, request.RequestHash);

        try
        {
            return await DeliverAsync(claimedId, request, notice, current, settings, cancellationToken);
        }
        catch
        {
            await store.ReleaseAsync(claimedId, CancellationToken.None);
            throw;
        }
    }

    private async Task<Result<AppNoticeDelivery>> DeliverAsync(
        Guid claimedId, IdentityAppNotice request, AppNotice notice, TelegramBotOptions current, AppNoticeOptions settings, CancellationToken cancellationToken)
    {
        var person = await users.FindByIdAsync(request.UserId, cancellationToken);
        var phone = person is { IsPhoneNumberConfirmed: true } ? PhoneNumberPolicy.Normalize(person.PhoneNumber) : null;
        var link = phone is null ? null : await links.FindLinkAsync(current.NormalizedBotUserName, phone, cancellationToken);
        if (link is null)
        {
            await store.ReleaseAsync(claimedId, cancellationToken);
            return Delivered(AppNoticeStatusConstants.NotLinked, null);
        }

        if (await store.IsMutedAsync(request.UserId, request.ClientId, cancellationToken))
        {
            await store.CompleteAsync(claimedId, AppNoticeStatusConstants.Muted, null, cancellationToken);
            return Delivered(AppNoticeStatusConstants.Muted, null);
        }

        if (await pace.WaitTurnAsync(link.ChatId, settings.MaxPaceWait, cancellationToken) is { } turnIn)
        {
            await store.ReleaseAsync(claimedId, cancellationToken);
            return Result<AppNoticeDelivery>.Fail(AppNoticeErrors.RateLimited(turnIn));
        }

        var culture = messages.CultureOf(notice.Culture);
        var sent = await sender.SendAsync(
            new TelegramNotice
            {
                Kind = Kind,
                UserId = request.UserId,
                ChatId = link.ChatId,
                Text = notice.Text,
                IsHtml = true,
                Culture = notice.Culture,
                IsKept = true,
                Links = [.. notice.Buttons.Select(button => new TelegramNoticeLink { Label = button.Text, Url = button.Url })],
                Buttons =
                [
                    new TelegramNoticeButton
                    {
                        Label = messages.Text(TelegramBotMessageKeyConstants.MuteNoticesButton, culture),
                        Action = MuteAction,
                        Payload = request.ClientId,
                        AllowedTelegramUserId = link.TelegramUserId,
                    },
                ],
            },
            cancellationToken);
        if (!sent.IsSent)
        {
            await store.ReleaseAsync(claimedId, cancellationToken);
            return Result<AppNoticeDelivery>.Fail(FailureOf(sent));
        }

        await store.CompleteAsync(claimedId, AppNoticeStatusConstants.Sent, sent.RecordId, cancellationToken);
        return Delivered(AppNoticeStatusConstants.Sent, sent.RecordId);
    }

    /// <summary>A repeated key: its first outcome, or a refusal for another body or a send still under way.</summary>
    private static Result<AppNoticeDelivery> Replay(IdentityAppNotice existing, string requestHash)
    {
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            return Result<AppNoticeDelivery>.Fail(AppNoticeErrors.KeyReused());

        return existing.Status switch
        {
            AppNoticeStatusConstants.Sent => Delivered(AppNoticeStatusConstants.Sent, existing.MessageRecordId),
            AppNoticeStatusConstants.Muted => Delivered(AppNoticeStatusConstants.Muted, null),
            _ => Result<AppNoticeDelivery>.Fail(AppNoticeErrors.InProgress()),
        };
    }

    /// <summary>Telegram's flood control is a rate limit, its outages and a missing bot a retry; anything else a refusal.</summary>
    private static AppError FailureOf(TelegramNoticeResult sent) => sent.ErrorCode switch
    {
        429 => AppNoticeErrors.RateLimited(sent.RetryAfter ?? TimeSpan.FromSeconds(1)),
        null or >= 500 => AppNoticeErrors.Unavailable("Telegram could not be reached, or the bot is off.", TimeSpan.FromSeconds(30)),
        _ => AppNoticeErrors.Refused(sent.FailureReason ?? "refused"),
    };

    private static Result<AppNoticeDelivery> Delivered(string status, Guid? id)
        => Result<AppNoticeDelivery>.Ok(new AppNoticeDelivery { Status = status, Id = id });

    /// <summary>The first rule <paramref name="notice"/> breaks, worded for the caller; null when it keeps them all.</summary>
    internal static string? FindProblem(AppNotice notice)
    {
        if (!Guid.TryParse(notice.Subject, out var subject) || subject == Guid.Empty)
            return "sub must be a person's id, a uuid.";
        if (string.IsNullOrWhiteSpace(notice.Text))
            return "text is required.";
        if (notice.Text.Length > AppNoticeConstants.MaxTextLength)
            return $"text holds at most {AppNoticeConstants.MaxTextLength} characters, tags included.";
        if (TelegramHtml.FindProblem(notice.Text) is { } html)
            return "text: " + html;
        if (string.IsNullOrEmpty(notice.IdempotencyKey) || notice.IdempotencyKey.Length > AppNoticeConstants.MaxIdempotencyKeyLength
            || notice.IdempotencyKey.Any(c => c is < '!' or > '~'))
            return $"idempotencyKey must be 1–{AppNoticeConstants.MaxIdempotencyKeyLength} printable ASCII characters with no spaces.";
        if (notice.Culture is { Length: > MaxCultureLength })
            return $"culture holds at most {MaxCultureLength} characters.";

        var buttons = notice.Buttons ?? [];
        if (buttons.Count > AppNoticeConstants.MaxButtons)
            return $"A notice carries at most {AppNoticeConstants.MaxButtons} buttons.";
        foreach (var button in buttons)
        {
            if (button is null || string.IsNullOrWhiteSpace(button.Text) || button.Text.Length > AppNoticeConstants.MaxButtonTextLength)
                return $"Each button needs text of 1–{AppNoticeConstants.MaxButtonTextLength} characters.";
            if (string.IsNullOrEmpty(button.Url) || button.Url.Length > AppNoticeConstants.MaxUrlLength
                || !Uri.TryCreate(button.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
                return $"Each button needs an absolute https or http url of at most {AppNoticeConstants.MaxUrlLength} characters.";
        }

        return null;
    }

    /// <summary>The hex SHA-256 of the body's meaning: the person, text, buttons and culture.</summary>
    private static string HashOf(AppNotice notice)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            Subject = Guid.Parse(notice.Subject).ToString("D"),
            notice.Text,
            Buttons = notice.Buttons.Select(button => new[] { button.Text, button.Url }),
            notice.Culture,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Provides the bot's phone links: the chat a number is linked to, and start links that link one. A start link is
/// <c>https://t.me/{bot}?start={token}</c> with 32 random characters; only the token's SHA-256 is stored, and making one
/// is rate-limited per number and per origin by <see cref="OtpOptions.RateLimitWindow"/>.
/// </summary>
/// <param name="repository">The links and start links.</param>
/// <param name="hasher">Keys the origin as the OTP store does.</param>
/// <param name="otp">The window between two requests.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="time">The clock expiry and windows are measured against.</param>
public sealed class TelegramLinkService(
    ITelegramLinkRepository repository,
    OtpCodeHasher hasher,
    OtpOptions otp,
    IOptionsMonitor<TelegramBotOptions> options,
    TimeProvider time)
{
    private const int TokenBytes = 24;

    /// <summary>Whether the bot runs.</summary>
    public bool IsEnabled => options.CurrentValue.IsEnabled;

    /// <summary>The chat <paramref name="phoneNumber"/> is linked to on the current bot, or null.</summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<long?> FindChatAsync(string phoneNumber, CancellationToken cancellationToken = default)
        => (await repository.FindLinkAsync(options.CurrentValue.NormalizedBotUserName, phoneNumber, cancellationToken))?.ChatId;

    /// <summary>Whether a start link was made within the window for <paramref name="phoneNumber"/> or from <paramref name="origin"/>.</summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="origin">Where the request came from; null checks the number only.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public Task<bool> IsRateLimitedAsync(string phoneNumber, string? origin, CancellationToken cancellationToken = default)
        => repository.HasRecentRequestAsync(
            options.CurrentValue.NormalizedBotUserName,
            phoneNumber,
            OriginHashOf(origin),
            time.GetUtcNow() - otp.RateLimitWindow,
            cancellationToken);

    /// <summary>Makes a start link for <paramref name="phoneNumber"/>; the shared contact must hold that number.</summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="purpose">What completing it leads to, one of <see cref="TelegramLinkPurposeConstants"/>.</param>
    /// <param name="origin">Where the request came from, for rate limiting; null when unknown.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The <c>t.me</c> URL to open.</returns>
    public async Task<string> CreateStartLinkAsync(string phoneNumber, string purpose, string? origin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var current = options.CurrentValue;
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = time.GetUtcNow();
        await repository.AddRequestAsync(
            new IdentityTelegramLinkRequest
            {
                Id = Guid.CreateVersion7(now),
                TokenHash = TokenHashOf(token),
                Purpose = purpose,
                BotUserName = current.NormalizedBotUserName,
                PhoneNumber = phoneNumber,
                OriginHash = OriginHashOf(origin),
                CreatedAt = now,
                ExpiresAt = now + current.LinkLifetime,
            },
            cancellationToken);

        return string.Create(CultureInfo.InvariantCulture, $"https://t.me/{current.BotUserName!.Trim().TrimStart('@')}?start={token}");
    }

    /// <summary>The hex SHA-256 a start token is stored and looked up by.</summary>
    /// <param name="token">The token from the link.</param>
    internal static string TokenHashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string? OriginHashOf(string? origin) => string.IsNullOrWhiteSpace(origin) ? null : hasher.Origin(origin);
}

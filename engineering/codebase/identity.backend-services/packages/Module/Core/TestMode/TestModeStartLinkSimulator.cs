using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>
/// Plays the person at a new start link while test mode is on: it feeds the bot's own <see cref="TelegramUpdateHandler"/> the
/// <c>/start</c> message and the shared contact, so the link, its checks and the code that follows run the real path, the
/// bot's replies landing in the <see cref="TestMessageSink"/>. The person's chat id is the digits of their number.
/// </summary>
/// <param name="services">The request's provider; the handler is resolved on demand, since it reaches the sign-in service that asks for this one.</param>
/// <param name="options">The switch.</param>
/// <param name="time">Dates the updates.</param>
internal sealed class TestModeStartLinkSimulator(IServiceProvider services, TestModeOptions options, TimeProvider time) : IPhoneSignInLinkSimulator
{
    /// <inheritdoc />
    public async Task<bool> TryCompleteAsync(string phoneNumber, string startLinkUrl, CultureInfo culture, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(startLinkUrl);
        ArgumentNullException.ThrowIfNull(culture);
        if (!options.IsEnabled)
            return false;

        var token = startLinkUrl[(startLinkUrl.IndexOf('=', StringComparison.Ordinal) + 1)..];
        var chatId = long.Parse(phoneNumber.TrimStart('+'), CultureInfo.InvariantCulture);
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(UpdateOf(chatId, culture, message => message.Text = "/start " + token), cancellationToken);
        await updates.HandleAsync(
            UpdateOf(chatId, culture, message => message.Contact = new Contact { PhoneNumber = phoneNumber, FirstName = "Test", UserId = chatId }),
            cancellationToken);
        return true;
    }

    private Update UpdateOf(long chatId, CultureInfo culture, Action<Message> shape)
    {
        var message = new Message
        {
            Id = 1,
            Date = time.GetUtcNow().UtcDateTime,
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            From = new User { Id = chatId, IsBot = false, FirstName = "Test", LanguageCode = culture.TwoLetterISOLanguageName },
        };
        shape(message);
        return new Update { Id = 1, Message = message };
    }
}

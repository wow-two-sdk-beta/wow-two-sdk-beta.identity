using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>
/// Handles the Approve and Deny buttons of a new device notice: the router has checked the presser is the contact's own
/// Telegram user and spent both buttons, so the answer goes to the request, and the notice reads what came of it.
/// </summary>
/// <param name="confirmations">Records the answer.</param>
/// <param name="messages">The bot's wording.</param>
public sealed class DeviceConfirmationDecisionHandler(DeviceConfirmationService confirmations, TelegramBotMessages messages) : ITelegramCallbackHandler
{
    /// <inheritdoc />
    public async Task<TelegramCallbackResult> HandleAsync(TelegramCallback callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var decision = TryRead(callback.Payload, out var id, out var contactUserId)
            ? await confirmations.DecideAsync(id, contactUserId, callback.Action == DeviceConfirmationService.ApproveAction, cancellationToken)
            : DeviceDecision.NoLongerOpen;
        var text = messages.Text(
            decision switch
            {
                DeviceDecision.Approved => TelegramBotMessageKeyConstants.DeviceApproved,
                DeviceDecision.Denied => TelegramBotMessageKeyConstants.DeviceDenied,
                _ => TelegramBotMessageKeyConstants.DeviceConfirmationClosed,
            },
            callback.Culture);
        return new TelegramCallbackResult { MessageText = text, AnswerText = text };
    }

    /// <summary>The payload of a request's button for one contact.</summary>
    /// <param name="id">The request's id.</param>
    /// <param name="contactUserId">The contact the button is for.</param>
    internal static string PayloadOf(Guid id, Guid contactUserId)
        => string.Create(CultureInfo.InvariantCulture, $"{id:N}:{contactUserId:N}");

    private static bool TryRead(string? payload, out Guid id, out Guid contactUserId)
    {
        id = default;
        contactUserId = default;
        var parts = payload?.Split(':');
        return parts is { Length: 2 }
            && Guid.TryParseExact(parts[0], "N", out id)
            && Guid.TryParseExact(parts[1], "N", out contactUserId);
    }
}

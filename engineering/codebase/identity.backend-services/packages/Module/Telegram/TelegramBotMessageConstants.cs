namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Holds the bot's built-in wording for <c>en</c>, <c>ru</c> and <c>uz</c>; configured texts override it.</summary>
internal static class TelegramBotMessageConstants
{
    /// <summary>Culture → key → text.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TelegramBotMessageKeyConstants.SharePrompt] = "Tap the button below to share your phone number. Your sign-in code follows here.",
                [TelegramBotMessageKeyConstants.ShareButton] = "Share my phone number",
                [TelegramBotMessageKeyConstants.NotOwnContact] = "Share your own number with the button below.",
                [TelegramBotMessageKeyConstants.NumberMismatch] = "This number differs from the one you entered. Start again from the sign-in page.",
                [TelegramBotMessageKeyConstants.LinkExpired] = "This link has expired or was already used. Start again from the sign-in page.",
                [TelegramBotMessageKeyConstants.Welcome] = "Start from the sign-in page: it sends you here with a link.",
                [TelegramBotMessageKeyConstants.CodeRecentlySent] = "A code was sent moments ago. Ask for a new one on the sign-in page.",
            },
            ["ru"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TelegramBotMessageKeyConstants.SharePrompt] = "Нажмите кнопку ниже, чтобы отправить свой номер телефона. Код для входа придёт сюда.",
                [TelegramBotMessageKeyConstants.ShareButton] = "Отправить мой номер",
                [TelegramBotMessageKeyConstants.NotOwnContact] = "Отправьте свой собственный номер кнопкой ниже.",
                [TelegramBotMessageKeyConstants.NumberMismatch] = "Этот номер не совпадает с введённым. Начните заново со страницы входа.",
                [TelegramBotMessageKeyConstants.LinkExpired] = "Ссылка устарела или уже использована. Начните заново со страницы входа.",
                [TelegramBotMessageKeyConstants.Welcome] = "Начните со страницы входа: она направит вас сюда по ссылке.",
                [TelegramBotMessageKeyConstants.CodeRecentlySent] = "Код был отправлен только что. Запросите новый на странице входа.",
            },
            ["uz"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TelegramBotMessageKeyConstants.SharePrompt] = "Telefon raqamingizni yuborish uchun quyidagi tugmani bosing. Kirish kodi shu yerga keladi.",
                [TelegramBotMessageKeyConstants.ShareButton] = "Raqamimni yuborish",
                [TelegramBotMessageKeyConstants.NotOwnContact] = "Quyidagi tugma orqali o'z raqamingizni yuboring.",
                [TelegramBotMessageKeyConstants.NumberMismatch] = "Bu raqam siz kiritgan raqamga mos kelmaydi. Kirish sahifasidan qaytadan boshlang.",
                [TelegramBotMessageKeyConstants.LinkExpired] = "Havola eskirgan yoki allaqachon ishlatilgan. Kirish sahifasidan qaytadan boshlang.",
                [TelegramBotMessageKeyConstants.Welcome] = "Kirish sahifasidan boshlang: u sizni havola orqali shu yerga yuboradi.",
                [TelegramBotMessageKeyConstants.CodeRecentlySent] = "Kod hozirgina yuborildi. Kirish sahifasida yangisini so'rang.",
            },
        };
}

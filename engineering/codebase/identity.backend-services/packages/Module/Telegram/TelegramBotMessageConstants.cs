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
                [TelegramBotMessageKeyConstants.SignUpSharePrompt] = "Tap the button below to share your phone number and finish signing up.",
                [TelegramBotMessageKeyConstants.SignUpDone] = "Your number is verified. Go back to the sign-up page: it continues on its own.",
                [TelegramBotMessageKeyConstants.SignUpFailed] = "This invitation can no longer be used. Ask for a new link.",
                [TelegramBotMessageKeyConstants.InvitationMismatch] = "This invitation is for another phone number.",
                [TelegramBotMessageKeyConstants.CountryNotAllowed] = "Phone numbers from this country cannot sign up here.",
                [TelegramBotMessageKeyConstants.LinkChangePending] = "This number is linked to another Telegram account. An admin of your organization must approve the change; then start again from the sign-in page.",
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
                [TelegramBotMessageKeyConstants.SignUpSharePrompt] = "Нажмите кнопку ниже, чтобы отправить свой номер телефона и завершить регистрацию.",
                [TelegramBotMessageKeyConstants.SignUpDone] = "Ваш номер подтверждён. Вернитесь на страницу регистрации: она продолжит сама.",
                [TelegramBotMessageKeyConstants.SignUpFailed] = "Это приглашение больше нельзя использовать. Попросите новую ссылку.",
                [TelegramBotMessageKeyConstants.InvitationMismatch] = "Это приглашение для другого номера телефона.",
                [TelegramBotMessageKeyConstants.CountryNotAllowed] = "Номера из этой страны не могут зарегистрироваться здесь.",
                [TelegramBotMessageKeyConstants.LinkChangePending] = "Этот номер привязан к другому аккаунту Telegram. Администратор вашей организации должен одобрить изменение; затем начните заново со страницы входа.",
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
                [TelegramBotMessageKeyConstants.SignUpSharePrompt] = "Telefon raqamingizni yuborib, ro'yxatdan o'tishni yakunlash uchun quyidagi tugmani bosing.",
                [TelegramBotMessageKeyConstants.SignUpDone] = "Raqamingiz tasdiqlandi. Ro'yxatdan o'tish sahifasiga qayting: u o'zi davom etadi.",
                [TelegramBotMessageKeyConstants.SignUpFailed] = "Bu taklifnomadan endi foydalanib bo'lmaydi. Yangi havola so'rang.",
                [TelegramBotMessageKeyConstants.InvitationMismatch] = "Bu taklifnoma boshqa telefon raqami uchun.",
                [TelegramBotMessageKeyConstants.CountryNotAllowed] = "Bu davlat raqamlari bilan bu yerda ro'yxatdan o'tib bo'lmaydi.",
                [TelegramBotMessageKeyConstants.LinkChangePending] = "Bu raqam boshqa Telegram akkauntiga bog'langan. Tashkilotingiz administratori o'zgarishni tasdiqlashi kerak; so'ng kirish sahifasidan qaytadan boshlang.",
            },
        };
}

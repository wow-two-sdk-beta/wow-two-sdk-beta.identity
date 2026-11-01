using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using static WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration.SignInRuleCodeConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>
/// Builds the declaration from its settings: the password method, then the phone code, each while it is on, with their
/// fields, the fe sdk rules and the messages in one locale.
/// </summary>
internal static class SignInDeclarationBuilder
{
    /// <summary>The sign-in request property holding the user name or email.</summary>
    public const string LoginField = "login";

    /// <summary>The sign-in request property holding the password.</summary>
    public const string PasswordField = "password";

    /// <summary>The phone sign-in request property holding the number.</summary>
    public const string PhoneField = "phone";

    /// <summary>The phone sign-in request property holding the code.</summary>
    public const string CodeField = "code";

    /// <summary>Builds the declaration for <paramref name="input"/> with its messages in <paramref name="locale"/>.</summary>
    /// <param name="input">The settings in effect.</param>
    /// <param name="locale">An offered locale (<see cref="SignInLocaleResolver.Locales"/>).</param>
    public static SignInOptionsDto Build(SignInDeclarationInput input, string locale)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);

        var methods = new List<SignInMethodDto>();
        if (input.Password is { } password)
            methods.Add(PasswordMethod(password, input.IsEmailLoginAllowed, locale));
        if (input.PhoneCountries is { } countries)
            methods.Add(PhoneCodeMethod(countries, input.PhoneCodeLength, locale));

        return new SignInOptionsDto
        {
            BrandName = input.BrandName,
            Theme = input.Theme,
            BackgroundUrl = string.IsNullOrEmpty(input.BackgroundUrl) ? null : input.BackgroundUrl,
            IsSignUpEnabled = input.IsSignUpEnabled,
            Locale = locale,
            Locales = SignInLocaleResolver.Locales,
            Methods = methods,
            Requirements = new SignInRequirementsDto
            {
                IsConfirmedEmailRequired = input.IsConfirmedEmailRequired,
                IsConfirmedPhoneNumberRequired = input.IsConfirmedPhoneNumberRequired,
            },
        };
    }

    /// <summary>
    /// The rules a new password meets, in the order <c>PasswordRulesValidator</c> checks them, each with the server's own
    /// message for it: length bounds, the required character kinds, distinct characters, and the account identifiers.
    /// </summary>
    /// <param name="rules">The password rules in effect.</param>
    /// <param name="locale">An offered locale.</param>
    public static IEnumerable<SignInRuleDto> PasswordRules(PasswordOptions rules, string locale)
    {
        ArgumentNullException.ThrowIfNull(rules);
        yield return Rule(
            Min,
            Text(IdentityErrorCodeConstants.PasswordTooShort, locale, ("MinLength", rules.MinLength)),
            ("min", rules.MinLength), ("unit", CharactersUnit));
        yield return Rule(
            Max,
            Text(IdentityErrorCodeConstants.PasswordTooLong, locale, ("MaxLength", rules.MaxLength)),
            ("max", rules.MaxLength), ("unit", CharactersUnit));
        if (rules.IsDigitRequired)
            yield return Rule(Pattern, Text(IdentityErrorCodeConstants.PasswordRequiresDigit, locale), ("pattern", "[0-9]"));
        if (rules.IsLowercaseRequired)
            yield return Rule(Pattern, Text(IdentityErrorCodeConstants.PasswordRequiresLower, locale), ("pattern", @"\p{Ll}"));
        if (rules.IsUppercaseRequired)
            yield return Rule(Pattern, Text(IdentityErrorCodeConstants.PasswordRequiresUpper, locale), ("pattern", @"\p{Lu}"));
        if (rules.IsNonAlphanumericRequired)
            yield return Rule(Pattern, Text(IdentityErrorCodeConstants.PasswordRequiresNonAlphanumeric, locale), ("pattern", @"[^\p{L}\p{Nd}]"));
        if (rules.RequiredUniqueChars > 1)
        {
            yield return Rule(
                Custom,
                Text(IdentityErrorCodeConstants.PasswordRequiresUniqueChars, locale, ("RequiredUniqueChars", rules.RequiredUniqueChars)),
                ("rule", DistinctCharactersRule), ("min", rules.RequiredUniqueChars));
        }

        if (rules.IsAccountIdentifierRejected)
        {
            yield return Rule(
                NotEqual,
                Text(IdentityErrorCodeConstants.PasswordMatchesAccount, locale),
                ("fields", new[] { "userName", "email" }), ("isCaseIgnored", true));
        }
    }

    /// <summary>The message <paramref name="key"/> names in <paramref name="locale"/>, else in <c>en</c>, with its placeholders filled.</summary>
    /// <param name="key">A <see cref="SignInDeclarationTextConstants"/> key or a password rule's error code.</param>
    /// <param name="locale">The locale.</param>
    /// <param name="values">The operands that fill <c>{Name}</c> placeholders.</param>
    public static string Text(string key, string locale, params (string Name, object Value)[] values)
    {
        var template = Find(SignInDeclarationTextConstants.Messages, locale, key)
            ?? Find(IdentityErrorTextConstants.Messages, locale, key)
            ?? Find(SignInDeclarationTextConstants.Messages, SignInLocaleResolver.DefaultLocale, key)
            ?? throw new InvalidOperationException($"The declaration has no '{key}' text.");
        foreach (var (name, value) in values)
            template = template.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return template;
    }

    private static SignInMethodDto PasswordMethod(PasswordOptions rules, bool isEmailLoginAllowed, string locale) => new()
    {
        Id = SignInMethodIdConstants.Password,
        Fields =
        [
            Field(
                LoginField,
                SignInFieldKindConstants.Text,
                Rule(Required, Text(isEmailLoginAllowed ? SignInDeclarationTextConstants.LoginRequired : SignInDeclarationTextConstants.UserNameLoginRequired, locale))),
            Field(PasswordField, SignInFieldKindConstants.Password, Rule(Required, Text(SignInDeclarationTextConstants.PasswordRequired, locale))),
        ],
        PasswordRules = [.. PasswordRules(rules, locale)],
    };

    private static SignInMethodDto PhoneCodeMethod(IReadOnlyList<string> countries, int codeLength, string locale) => new()
    {
        Id = SignInMethodIdConstants.PhoneCode,
        Channels = [OtpChannelNameConstants.Telegram],
        Fields =
        [
            Field(PhoneField, SignInFieldKindConstants.Phone, Rule(Required, Text(SignInDeclarationTextConstants.PhoneRequired, locale))) with
            {
                Countries = [.. countries.Select(country => country.Trim().ToUpperInvariant())],
            },
            Field(
                CodeField,
                SignInFieldKindConstants.Pin,
                Rule(Required, Text(SignInDeclarationTextConstants.CodeRequired, locale)),
                Rule(Length, Text(SignInDeclarationTextConstants.CodeLength, locale, ("Length", codeLength)), ("length", codeLength), ("unit", CharactersUnit))),
        ],
    };

    private static SignInFieldDto Field(string name, string kind, params SignInRuleDto[] rules)
        => new() { Name = name, Kind = kind, Rules = rules };

    private static SignInRuleDto Rule(string code, string message, params (string Name, object Value)[] operands) => new()
    {
        Code = code,
        Message = message,
        Params = operands.Length == 0 ? null : operands.ToDictionary(operand => operand.Name, operand => operand.Value, StringComparer.Ordinal),
    };

    private static string? Find(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> catalogue, string locale, string key)
        => catalogue.TryGetValue(locale, out var texts) && texts.TryGetValue(key, out var text) ? text : null;
}

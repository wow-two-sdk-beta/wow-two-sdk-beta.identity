using System.Globalization;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Provides the bot's wording in the person's Telegram language: configured texts first, then the built-in ones, falling
/// back through the culture's parents, <see cref="TelegramBotOptions.DefaultCulture"/> and <c>en</c>.
/// </summary>
/// <param name="options">The bot; <c>Identity:Telegram:Messages</c> reloads live.</param>
public sealed class TelegramBotMessages(IOptionsMonitor<TelegramBotOptions> options)
{
    /// <summary>The culture of a Telegram <c>language_code</c>, such as <c>uz</c>; the default culture when it is unknown.</summary>
    /// <param name="languageCode">The person's Telegram language, or null.</param>
    public CultureInfo CultureOf(string? languageCode)
    {
        foreach (var name in new[] { languageCode, options.CurrentValue.DefaultCulture, "en" })
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            try
            {
                return CultureInfo.GetCultureInfo(name);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return CultureInfo.InvariantCulture;
    }

    /// <summary>The text for <paramref name="key"/> in <paramref name="culture"/>.</summary>
    /// <param name="key">One of <see cref="TelegramBotMessageKeyConstants"/>.</param>
    /// <param name="culture">The person's culture.</param>
    public string Text(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);

        var current = options.CurrentValue;
        foreach (var name in Cultures(culture, current.DefaultCulture))
        {
            if (current.Messages.TryGetValue(name, out var configured) && configured.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text))
                return text;
            if (TelegramBotMessageConstants.Texts.TryGetValue(name, out var builtIn) && builtIn.TryGetValue(key, out text))
                return text;
        }

        throw new InvalidOperationException($"The Telegram bot has no text for '{key}'.");
    }

    private static IEnumerable<string> Cultures(CultureInfo requested, string defaultCulture)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { requested.Name, defaultCulture, "en" })
        {
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(start);
            }
            catch (CultureNotFoundException)
            {
                continue;
            }

            for (; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
            {
                if (seen.Add(culture.Name))
                    yield return culture.Name;
            }
        }
    }
}

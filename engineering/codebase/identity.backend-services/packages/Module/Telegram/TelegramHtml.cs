using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Checks text against Telegram's HTML subset (<c>parse_mode</c> <c>HTML</c>) before it is sent, so a caller hears of a broken
/// tag from Identity rather than as Telegram's refusal: the supported tags only, each closed in order, the attributes they
/// take, and every <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> outside a tag written as an entity.
/// </summary>
internal static partial class TelegramHtml
{
    private static readonly HashSet<string> PlainTags = new(StringComparer.Ordinal) { "b", "strong", "i", "em", "u", "ins", "s", "strike", "del", "tg-spoiler", "pre" };

    /// <summary>What breaks the subset, worded for the caller; null when <paramref name="text"/> keeps to it.</summary>
    /// <param name="text">The text to check.</param>
    public static string? FindProblem(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var open = new Stack<string>();
        var index = 0;
        while (index < text.Length)
        {
            switch (text[index])
            {
                case '<':
                    var end = text.IndexOf('>', index + 1);
                    if (end < 0)
                        return "A '<' opens no tag: write it as &lt;.";
                    if (TagProblem(text[(index + 1)..end], open) is { } problem)
                        return problem;
                    index = end + 1;
                    break;
                case '>':
                    return "A '>' outside a tag: write it as &gt;.";
                case '&':
                    var entity = Entity().Match(text, index);
                    if (!entity.Success)
                        return "A '&' starts no entity: write it as &amp;.";
                    index += entity.Length;
                    break;
                default:
                    index++;
                    break;
            }
        }

        return open.Count == 0 ? null : $"The tag <{open.Peek()}> is never closed.";
    }

    private static string? TagProblem(string tag, Stack<string> open)
    {
        if (tag.StartsWith('/'))
        {
            var closing = tag[1..].Trim();
            if (open.Count == 0 || !string.Equals(open.Peek(), closing, StringComparison.Ordinal))
                return $"The closing tag </{closing}> matches no open tag.";
            open.Pop();
            return null;
        }

        var name = tag.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        var rest = tag[name.Length..];
        var position = 0;
        while (position < rest.Length)
        {
            var attribute = Attribute().Match(rest, position);
            if (!attribute.Success || attribute.Length == 0)
                return $"The tag <{name}> has an attribute Telegram cannot read.";
            if (attribute.Groups["name"].Success)
            {
                var value = attribute.Groups["double"].Success ? attribute.Groups["double"].Value
                    : attribute.Groups["single"].Success ? attribute.Groups["single"].Value
                    : attribute.Groups["bare"].Success ? attribute.Groups["bare"].Value
                    : null;
                attributes[attribute.Groups["name"].Value] = value;
            }

            position += attribute.Length;
        }

        var problem = name switch
        {
            _ when PlainTags.Contains(name) => attributes.Count == 0 ? null : $"The tag <{name}> takes no attributes.",
            "a" => attributes.Count == 1 && !string.IsNullOrWhiteSpace(attributes.GetValueOrDefault("href")) ? null : "The tag <a> takes one href.",
            "span" => attributes.Count == 1 && attributes.GetValueOrDefault("class") == "tg-spoiler" ? null : "The tag <span> takes only class=\"tg-spoiler\".",
            "code" => attributes.Count == 0 || (attributes.Count == 1 && attributes.GetValueOrDefault("class") is { } language && language.StartsWith("language-", StringComparison.Ordinal))
                ? null
                : "The tag <code> takes only class=\"language-…\".",
            "blockquote" => attributes.Count == 0 || (attributes.Count == 1 && attributes.ContainsKey("expandable")) ? null : "The tag <blockquote> takes only expandable.",
            "tg-emoji" => attributes.Count == 1 && !string.IsNullOrWhiteSpace(attributes.GetValueOrDefault("emoji-id")) ? null : "The tag <tg-emoji> takes one emoji-id.",
            _ => $"Telegram's HTML has no tag <{name}>.",
        };
        if (problem is null)
            open.Push(name);
        return problem;
    }

    [GeneratedRegex("\\G&(?:lt|gt|amp|quot|#[0-9]{1,7}|#x[0-9a-fA-F]{1,6});")]
    private static partial Regex Entity();

    [GeneratedRegex("\\G\\s+(?:(?<name>[a-z][a-z-]*)(?:\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)'|(?<bare>[^\\s\"'>]+)))?)?|\\G\\s*$")]
    private static partial Regex Attribute();
}

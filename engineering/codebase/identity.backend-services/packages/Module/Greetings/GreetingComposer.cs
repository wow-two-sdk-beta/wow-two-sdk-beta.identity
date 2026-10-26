using WoW.Two.Sdk.Beta.Be.Identity.Greetings;

namespace WoW.Two.Sdk.Beta.Be.Identity.Module.Greetings;

/// <summary>Composes greetings — the module's pure logic.</summary>
internal static class GreetingComposer
{
    /// <summary>Composes the greeting for <paramref name="name"/>, or for the world when it is blank.</summary>
    public static GreetingModel Compose(string? name, DateTimeOffset now)
    {
        var trimmed = name?.Trim();
        var who = string.IsNullOrEmpty(trimmed) ? "world" : trimmed;

        return new GreetingModel { Message = $"Hello, {who}!", GreetedAtUtc = now };
    }
}

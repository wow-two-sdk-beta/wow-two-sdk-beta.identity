namespace WoW.Two.Sdk.Beta.Be.Identity.Greetings;

/// <summary>Represents a greeting the capability served.</summary>
public sealed record GreetingModel
{
    /// <summary>Gets the greeting text.</summary>
    public required string Message { get; init; }

    /// <summary>Gets when the greeting was served (UTC).</summary>
    public required DateTimeOffset GreetedAtUtc { get; init; }
}

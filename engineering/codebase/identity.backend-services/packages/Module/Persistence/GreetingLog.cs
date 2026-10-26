namespace WoW.Two.Sdk.Beta.Be.Identity.Module.Persistence;

/// <summary>Represents a greeting the module served — the one row the module stores.</summary>
internal sealed class GreetingLog
{
    /// <summary>Holds the table this entity maps to.</summary>
    public const string TableName = "identity_greeting_logs";

    /// <summary>Gets or sets the row key.</summary>
    public required Guid Id { get; set; }

    /// <summary>Gets or sets the greeting text that was returned.</summary>
    public required string Message { get; set; }

    /// <summary>Gets or sets when the greeting was served (UTC).</summary>
    public required DateTimeOffset GreetedAtUtc { get; set; }
}

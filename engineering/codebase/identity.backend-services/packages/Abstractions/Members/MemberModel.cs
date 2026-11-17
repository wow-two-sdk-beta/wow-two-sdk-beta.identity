using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Members;

/// <summary>One member of an organization as a service reads them (<see cref="IMemberBroker"/>), and an item of the route's answer.</summary>
public sealed record MemberModel
{
    /// <summary>The person's <c>sub</c> at the instance: their user id, a uuid.</summary>
    [JsonPropertyName("sub")]
    public required string Subject { get; init; }

    /// <summary>The person's role in the organization, in the catalog's spelling, such as <c>owner</c> or <c>editor</c>.</summary>
    public required string Role { get; init; }

    /// <summary>
    /// Whether the person's confirmed phone is linked to the instance's Telegram bot, so an app notice can reach them; false
    /// while the instance has no bot.
    /// </summary>
    public bool HasTelegramLink { get; init; }
}

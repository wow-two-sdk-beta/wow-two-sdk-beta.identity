using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Represents one <c>admin invite</c> run as its last stdout line: <c>admin-result {json}</c>.</summary>
/// <remarks>Exit <c>0</c> created · <c>1</c> refused (no such organization) · <c>2</c> failed (a usage error, an invalid role or phone, or an unreachable database).</remarks>
public sealed record AdminInviteResult
{
    /// <summary>Gets the verb: <c>invite</c>.</summary>
    [JsonPropertyOrder(0)]
    public string Verb { get; init; } = AdminCommand.InviteVerb;

    /// <summary>Gets the process exit code.</summary>
    [JsonPropertyOrder(1)]
    public required int Exit { get; init; }

    /// <summary>Gets the outcome: <c>created</c>, <c>refused</c> or <c>failed</c>.</summary>
    [JsonPropertyOrder(2)]
    public required string Status { get; init; }

    /// <summary>Gets the organization's id once found.</summary>
    [JsonPropertyOrder(3)]
    public Guid? OrganizationId { get; init; }

    /// <summary>Gets the invitation's id once made.</summary>
    [JsonPropertyOrder(4)]
    public Guid? InvitationId { get; init; }

    /// <summary>Gets the role the person joins with, once made.</summary>
    [JsonPropertyOrder(5)]
    public string? Role { get; init; }

    /// <summary>Gets the link that redeems it, carrying its token: hand it to the person, once.</summary>
    [JsonPropertyOrder(6)]
    public string? Link { get; init; }

    /// <summary>Gets the token alone, given only when the instance has no link template (no <c>Identity:Server:Issuer</c>).</summary>
    [JsonPropertyOrder(7)]
    public string? Token { get; init; }

    /// <summary>Gets when the invitation stops working (UTC).</summary>
    [JsonPropertyOrder(8)]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets why the run was refused or failed; absent otherwise.</summary>
    [JsonPropertyOrder(9)]
    public string? Error { get; init; }

    /// <summary>Gets the run's wall time in milliseconds.</summary>
    [JsonPropertyOrder(10)]
    public long DurationMs { get; init; }

    /// <summary>Renders the result as compact camelCase JSON; absent fields are left out.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, AdminJsonContext.Default.AdminInviteResult);

    /// <summary>Renders the <c>admin-result {json}</c> line the command prints last.</summary>
    public string ToResultLine() => AdminCreateResult.ResultLinePrefix + ToJson();

    /// <summary>Finds the last line starting <c>admin-result </c> in a command's output and parses it; null when none is there.</summary>
    /// <param name="output">Everything the command wrote to standard output.</param>
    public static AdminInviteResult? FromOutput(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var line = output.Split('\n').Select(candidate => candidate.Trim()).LastOrDefault(candidate => candidate.StartsWith(AdminCreateResult.ResultLinePrefix, StringComparison.Ordinal));
        if (line is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize(line[AdminCreateResult.ResultLinePrefix.Length..], AdminJsonContext.Default.AdminInviteResult);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

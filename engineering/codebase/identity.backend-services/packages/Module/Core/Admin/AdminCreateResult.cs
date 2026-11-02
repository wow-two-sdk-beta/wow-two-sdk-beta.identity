using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Represents one <c>admin create</c> run as its last stdout line: <c>admin-result {json}</c>.</summary>
/// <remarks>Exit <c>0</c> created or present · <c>1</c> refused · <c>2</c> failed (a usage error, an invalid request or an unreachable database).</remarks>
public sealed record AdminCreateResult
{
    /// <summary>Holds the prefix of the result line: <c>admin-result </c>.</summary>
    public const string ResultLinePrefix = "admin-result ";

    /// <summary>Gets the verb: <c>create</c>.</summary>
    [JsonPropertyOrder(0)]
    public string Verb { get; init; } = AdminCommand.CreateVerb;

    /// <summary>Gets the process exit code.</summary>
    [JsonPropertyOrder(1)]
    public required int Exit { get; init; }

    /// <summary>Gets the outcome: <c>created</c>, <c>present</c>, <c>refused</c> or <c>failed</c>.</summary>
    [JsonPropertyOrder(2)]
    public required string Status { get; init; }

    /// <summary>Gets the admin's id once they exist, or the named person's on a refusal that found them.</summary>
    [JsonPropertyOrder(3)]
    public Guid? UserId { get; init; }

    /// <summary>Gets whether this run created the person.</summary>
    [JsonPropertyOrder(4)]
    public bool IsUserCreated { get; init; }

    /// <summary>Gets whether this run gave the person the instance role <c>admin</c>; false when they held it already.</summary>
    [JsonPropertyOrder(5)]
    public bool IsRoleGranted { get; init; }

    /// <summary>Gets why the run was refused or failed; absent otherwise.</summary>
    [JsonPropertyOrder(6)]
    public string? Error { get; init; }

    /// <summary>Gets the run's wall time in milliseconds.</summary>
    [JsonPropertyOrder(7)]
    public long DurationMs { get; init; }

    /// <summary>Renders the result as compact camelCase JSON; absent fields are left out.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, AdminJsonContext.Default.AdminCreateResult);

    /// <summary>Renders the <c>admin-result {json}</c> line the command prints last.</summary>
    public string ToResultLine() => ResultLinePrefix + ToJson();

    /// <summary>Finds the last line starting <c>admin-result </c> in a command's output and parses it; null when none is there.</summary>
    /// <param name="output">Everything the command wrote to standard output.</param>
    public static AdminCreateResult? FromOutput(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var line = output.Split('\n').Select(candidate => candidate.Trim()).LastOrDefault(candidate => candidate.StartsWith(ResultLinePrefix, StringComparison.Ordinal));
        if (line is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize(line[ResultLinePrefix.Length..], AdminJsonContext.Default.AdminCreateResult);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The source-generated JSON of <see cref="AdminCreateResult"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AdminCreateResult))]
internal sealed partial class AdminJsonContext : JsonSerializerContext;

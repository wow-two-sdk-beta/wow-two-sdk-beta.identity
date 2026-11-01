using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>
/// Answers <c>app admin create</c> from a host's own services: creates the instance's first organization and its owner
/// (<see cref="FirstOwnerService"/>), non-interactive and idempotent. It never takes a password: the owner signs in by phone,
/// or sets one through the reset link.
/// </summary>
/// <remarks>
///   - <c>admin create --slug ocharo --name Ocharo [--organization-id &lt;uuid&gt;] --display-name "Lola Karimova"
///     [--phone +998901234567] [--email owner@example.com] [--user-id &lt;uuid&gt;]</c>; the owner needs a phone, an email or both
///   - exits <c>0</c> created or present · <c>1</c> refused (the handle, an id, the email or the phone belongs to someone
///     else) · <c>2</c> failed (a usage error, an invalid value or an unreachable database); the last stdout line is one
///     <c>admin-result {json}</c> line, the human report goes to stderr
///   - starts no listener and no hosted service; the schema must be current (<c>migrate</c> first)
///   - the service host dispatches it as a host verb: <c>verbs.AddHostVerb(AdminCommand.Name, …)</c>
/// </remarks>
public static class AdminCommand
{
    /// <summary>Holds the command's name: the first argument that selects it.</summary>
    public const string Name = "admin";

    /// <summary>Holds the one verb: <c>create</c>.</summary>
    public const string CreateVerb = "create";

    /// <summary>Holds the usage line printed for any other arguments.</summary>
    public const string Usage = "usage: admin create --slug <handle> --name <organization name> [--organization-id <uuid>] "
        + "--display-name <owner's name> [--phone <E.164>] [--email <address>] [--user-id <uuid>]";

    private static readonly string[] Options = ["--slug", "--name", "--organization-id", "--display-name", "--phone", "--email", "--user-id"];

    /// <summary>Runs one invocation over a built host's services, writing the human report to <paramref name="error"/> and the result line last to <paramref name="output"/>.</summary>
    /// <param name="services">The built host's root provider; never started, never disposed.</param>
    /// <param name="arguments">The arguments after <c>admin</c>: <c>create</c> and its options.</param>
    /// <param name="output">Standard output: the result line only.</param>
    /// <param name="error">Standard error: the human report.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The exit code: <c>0</c> created or present · <c>1</c> refused · <c>2</c> failed.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output, TextWriter error, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var clock = Stopwatch.StartNew();
        AdminCreateResult result;
        if (Parse(arguments, out var request) is { } usage)
        {
            result = Failed(usage + Environment.NewLine + Usage);
        }
        else
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var owner = scope.ServiceProvider.GetService<FirstOwnerService>()
                    ?? throw new InvalidOperationException("admin create needs the organizations slice: call AddOrganizations().");
                result = Map(await owner.CreateAsync(request!, ct));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                result = Failed($"{exception.GetType().Name}: {exception.Message}");
            }
        }

        result = result with { DurationMs = clock.ElapsedMilliseconds };
        WriteHuman(result, error);
        await output.WriteLineAsync(result.ToResultLine()).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
        await error.FlushAsync(ct).ConfigureAwait(false);
        return result.Exit;
    }

    /// <summary>Reads <c>create</c> and its options into a request; the problem when the arguments make none.</summary>
    internal static string? Parse(IReadOnlyList<string> arguments, out FirstOwnerRequest? request)
    {
        request = null;
        if (arguments.Count == 0 || !string.Equals(arguments[0], CreateVerb, StringComparison.Ordinal))
            return arguments.Count == 0 ? "missing verb" : $"unknown verb '{arguments[0]}'";

        var rest = arguments.Skip(1).ToList();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rest.Count; i++)
        {
            var argument = rest[i];
            var split = argument.IndexOf('=', StringComparison.Ordinal);
            var option = split > 0 ? argument[..split] : argument;
            if (!Options.Contains(option, StringComparer.Ordinal))
                return $"unknown option '{option}'";

            string value;
            if (split > 0)
                value = argument[(split + 1)..];
            else if (i + 1 < rest.Count && !rest[i + 1].StartsWith("--", StringComparison.Ordinal))
                value = rest[++i];
            else
                return $"{option} needs a value";

            if (!values.TryAdd(option, value))
                return $"{option} is given twice";
        }

        foreach (var required in new[] { "--slug", "--name", "--display-name" })
        {
            if (!values.ContainsKey(required))
                return $"{required} is required";
        }

        if (!TryId(values, "--organization-id", out var organizationId) || !TryId(values, "--user-id", out var userId))
            return "--organization-id and --user-id take a uuid";

        request = new FirstOwnerRequest
        {
            Slug = values["--slug"],
            OrganizationName = values["--name"],
            OrganizationId = organizationId,
            DisplayName = values["--display-name"],
            PhoneNumber = values.GetValueOrDefault("--phone"),
            Email = values.GetValueOrDefault("--email"),
            UserId = userId,
        };
        return null;
    }

    private static bool TryId(Dictionary<string, string> values, string option, out Guid? id)
    {
        id = null;
        if (!values.TryGetValue(option, out var value))
            return true;
        if (!Guid.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
            return false;

        id = parsed;
        return true;
    }

    private static AdminCreateResult Map(FirstOwnerResult owner) => new()
    {
        Exit = owner.Status switch
        {
            FirstOwnerStatus.Created or FirstOwnerStatus.Present => 0,
            FirstOwnerStatus.Refused => 1,
            _ => 2,
        },
        Status = owner.Status switch
        {
            FirstOwnerStatus.Created => "created",
            FirstOwnerStatus.Present => "present",
            FirstOwnerStatus.Refused => "refused",
            _ => "failed",
        },
        OrganizationId = owner.OrganizationId,
        UserId = owner.UserId,
        IsOrganizationCreated = owner.IsOrganizationCreated,
        IsOwnerCreated = owner.IsOwnerCreated,
        Error = owner.Error,
    };

    private static AdminCreateResult Failed(string message) => new() { Exit = 2, Status = "failed", Error = message };

    private static void WriteHuman(AdminCreateResult result, TextWriter error)
    {
        var mark = result.Exit == 0 ? "✓" : "✗";
        error.WriteLine($"{mark} admin create: {result.Status} (exit {result.Exit.ToString(CultureInfo.InvariantCulture)})");
        if (result.OrganizationId is { } organizationId)
            error.WriteLine($"  organization {organizationId}: {(result.IsOrganizationCreated ? "created" : "present")}");
        if (result.UserId is { } userId)
            error.WriteLine($"  owner        {userId}: {(result.IsOwnerCreated ? "created" : "present")}");
        if (result.Error is { } message)
            error.WriteLine($"  {message}");
    }
}

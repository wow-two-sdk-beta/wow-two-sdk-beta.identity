using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>
/// Answers <c>app admin create</c> from a host's own services: creates the instance's first admin, the person holding the
/// instance role <c>admin</c> (<see cref="InstanceAdminService"/>), once per instance, non-interactive and idempotent. It
/// never takes a password: the admin signs in by phone, or sets one through the reset link. Organizations are created by
/// that admin in the console, and invitations by an organization's owners and admins there, never here.
/// </summary>
/// <remarks>
///   - <c>admin create --display-name "Lola Karimova" [--phone +998901234567] [--email admin@example.com]
///     [--user-id &lt;uuid&gt;]</c>; the admin needs a phone, an email or both
///   - exits <c>0</c> created, or present when the named person holds <c>admin</c> already · <c>1</c> refused (another
///     person holds <c>admin</c>, or an id, the email or the phone belongs to someone else) · <c>2</c> failed (a usage
///     error, an invalid value or an unreachable database)
///   - the last stdout line is one <c>admin-result {json}</c> line, the human report goes to stderr
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
    public const string Usage = "usage: admin create --display-name <admin's name> [--phone <E.164>] [--email <address>] [--user-id <uuid>]; "
        + "a phone, an email or both";

    private static readonly string[] CreateOptions = ["--display-name", "--phone", "--email", "--user-id"];

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
        var result = await CreateAsync(services, arguments, ct) with { DurationMs = clock.ElapsedMilliseconds };
        WriteHuman(result, error);
        await output.WriteLineAsync(result.ToResultLine()).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
        await error.FlushAsync(ct).ConfigureAwait(false);
        return result.Exit;
    }

    /// <summary>Reads <c>create</c> and its options into a request; the problem when the arguments make none.</summary>
    internal static string? Parse(IReadOnlyList<string> arguments, out InstanceAdminRequest? request)
    {
        request = null;
        if (arguments.Count == 0 || !string.Equals(arguments[0], CreateVerb, StringComparison.Ordinal))
            return arguments.Count == 0 ? "missing verb" : $"unknown verb '{arguments[0]}'";
        if (ReadOptions(arguments, CreateOptions, out var values) is { } problem)
            return problem;
        if (!values.ContainsKey("--display-name"))
            return "--display-name is required";
        if (!TryId(values, "--user-id", out var userId))
            return "--user-id takes a uuid";

        request = new InstanceAdminRequest
        {
            DisplayName = values["--display-name"],
            PhoneNumber = values.GetValueOrDefault("--phone"),
            Email = values.GetValueOrDefault("--email"),
            UserId = userId,
        };
        return null;
    }

    /// <summary>Reads the options after the verb, in <c>--name value</c> or <c>--name=value</c> form; the problem when one is unknown, bare or repeated.</summary>
    private static string? ReadOptions(IReadOnlyList<string> arguments, string[] options, out Dictionary<string, string> values)
    {
        var rest = arguments.Skip(1).ToList();
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rest.Count; i++)
        {
            var argument = rest[i];
            var split = argument.IndexOf('=', StringComparison.Ordinal);
            var option = split > 0 ? argument[..split] : argument;
            if (!options.Contains(option, StringComparer.Ordinal))
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

    private static async Task<AdminCreateResult> CreateAsync(IServiceProvider services, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (Parse(arguments, out var request) is { } usage)
            return new AdminCreateResult { Exit = 2, Status = "failed", Error = usage + Environment.NewLine + Usage };

        try
        {
            await using var scope = services.CreateAsyncScope();
            var admins = scope.ServiceProvider.GetService<InstanceAdminService>()
                ?? throw new InvalidOperationException("admin create needs the roles slice: call AddRoles().");
            return Map(await admins.CreateAsync(request!, ct));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new AdminCreateResult { Exit = 2, Status = "failed", Error = $"{exception.GetType().Name}: {exception.Message}" };
        }
    }

    private static AdminCreateResult Map(InstanceAdminResult admin) => new()
    {
        Exit = admin.Status switch
        {
            InstanceAdminStatus.Created or InstanceAdminStatus.Present => 0,
            InstanceAdminStatus.Refused => 1,
            _ => 2,
        },
        Status = admin.Status switch
        {
            InstanceAdminStatus.Created => "created",
            InstanceAdminStatus.Present => "present",
            InstanceAdminStatus.Refused => "refused",
            _ => "failed",
        },
        UserId = admin.UserId,
        IsUserCreated = admin.IsUserCreated,
        IsRoleGranted = admin.IsRoleGranted,
        Error = admin.Error,
    };

    private static void WriteHuman(AdminCreateResult result, TextWriter error)
    {
        var mark = result.Exit == 0 ? "✓" : "✗";
        error.WriteLine($"{mark} admin create: {result.Status} (exit {result.Exit.ToString(CultureInfo.InvariantCulture)})");
        if (result.UserId is { } userId)
        {
            error.WriteLine($"  person {userId}: {(result.IsUserCreated ? "created" : "present")}");
            if (result.Exit == 0)
                error.WriteLine($"  role   {InstanceRoleConstants.Admin}: {(result.IsRoleGranted ? "granted" : "held")}");
        }

        if (result.Error is { } message)
            error.WriteLine($"  {message}");
    }
}

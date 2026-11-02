using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Identity.PasswordHashing.Argon2;

/// <summary>Argon2id password hasher (OWASP-recommended); implements <see cref="IPasswordHasher{TUser}"/> so it slots into ASP.NET Core Identity.</summary>
/// <typeparam name="TUser">User type (any class).</typeparam>
/// <remarks>
/// Each hash holds 19 MiB while it runs, so at most <see cref="DefaultMaxConcurrentHashes"/> run at once per hasher (one
/// per process, as a singleton): the rest wait up to <see cref="DefaultQueueTimeout"/> for a slot, then fail with a
/// <c>429</c> application error rather than let a burst of sign-ins run the process out of memory.
/// </remarks>
public sealed class Argon2PasswordHasher<TUser> : IPasswordHasher<TUser> where TUser : class
{
    /// <summary>The hashes that run at once by default; four hold about 76 MiB.</summary>
    public const int DefaultMaxConcurrentHashes = 4;

    /// <summary>How long a hash waits for a free slot by default before it fails with a <c>429</c>.</summary>
    public static readonly TimeSpan DefaultQueueTimeout = TimeSpan.FromSeconds(5);

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 4;        // OWASP 2024 baseline
    private const int MemoryKb = 19_456;     // 19 MiB
    private const int Parallelism = 1;

    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _queueTimeout;

    /// <summary>Create the hasher with <see cref="DefaultMaxConcurrentHashes"/> slots and <see cref="DefaultQueueTimeout"/>.</summary>
    public Argon2PasswordHasher()
        : this(DefaultMaxConcurrentHashes, DefaultQueueTimeout)
    {
    }

    /// <summary>Create the hasher with its own bound on concurrent hashes.</summary>
    /// <param name="maxConcurrentHashes">The hashes that run at once; at least 1.</param>
    /// <param name="queueTimeout">How long a hash waits for a free slot before it fails with a <c>429</c>; zero fails at once.</param>
    public Argon2PasswordHasher(int maxConcurrentHashes, TimeSpan queueTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentHashes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueTimeout, TimeSpan.Zero);
        _slots = new SemaphoreSlim(maxConcurrentHashes, maxConcurrentHashes);
        _queueTimeout = queueTimeout;
    }

    /// <inheritdoc />
    public string HashPassword(TUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashCore(password, salt);
        return $"$argon2id$v=19$m={MemoryKb},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <inheritdoc />
    /// <remarks>
    /// Verifies with the parameters recorded in the hash, so raising the cost never locks out existing users; a match
    /// recorded with other parameters returns <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>.
    /// </remarks>
    public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(hashedPassword);
        ArgumentNullException.ThrowIfNull(providedPassword);

        var parts = hashedPassword.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || parts[0] != "argon2id" || !TryReadCost(parts[2], out var memoryKb, out var iterations, out var parallelism))
            return PasswordVerificationResult.Failed;

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            if (expected.Length is < 16 or > 64)
                return PasswordVerificationResult.Failed;

            var actual = HashCore(providedPassword, salt, memoryKb, iterations, parallelism, expected.Length);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                return PasswordVerificationResult.Failed;

            return memoryKb == MemoryKb && iterations == Iterations && parallelism == Parallelism && expected.Length == HashSize
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }

    private byte[] HashCore(string password, byte[] salt)
        => HashCore(password, salt, MemoryKb, Iterations, Parallelism, HashSize);

    private byte[] HashCore(string password, byte[] salt, int memoryKb, int iterations, int parallelism, int hashSize)
    {
        // The hasher's contract is synchronous, so the wait is too; it holds a thread, never the 19 MiB.
        if (!_slots.Wait(_queueTimeout))
        {
            throw AppError.Of(
                AppErrorType.TooManyRequests,
                "Too many passwords are being checked at once; try again in a moment.",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["retryAfter"] = "5" }).ToException();
        }

        try
        {
            using var argon = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = parallelism,
                MemorySize = memoryKb,
                Iterations = iterations,
            };
            return argon.GetBytes(hashSize);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>Reads <c>m=…,t=…,p=…</c>, bounding each value so a stored hash cannot demand unbounded work.</summary>
    private static bool TryReadCost(string segment, out int memoryKb, out int iterations, out int parallelism)
    {
        memoryKb = iterations = parallelism = 0;
        foreach (var pair in segment.Split(','))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0
                || !int.TryParse(pair.AsSpan(separator + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return false;

            switch (pair[..separator])
            {
                case "m" when value is > 0 and <= 1_048_576: memoryKb = value; break;
                case "t" when value is > 0 and <= 64: iterations = value; break;
                case "p" when value is > 0 and <= 64: parallelism = value; break;
                default: return false;
            }
        }

        return memoryKb > 0 && iterations > 0 && parallelism > 0;
    }
}

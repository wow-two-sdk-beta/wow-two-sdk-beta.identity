using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.PasswordHashing.Argon2;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>The Argon2id hasher bounds the hashes that hold their 19 MiB at once, and the OTP defaults space the resends.</summary>
public sealed class Argon2PasswordHasherTests
{
    [Fact]
    public async Task Hash_ShouldAnswer429_WhenEverySlotStaysTaken()
    {
        var hasher = new Argon2PasswordHasher<object>(maxConcurrentHashes: 1, queueTimeout: TimeSpan.Zero);
        using var start = new Barrier(4);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            try
            {
                hasher.HashPassword(new object(), "correct horse battery");
                return (AppErrorType?)null;
            }
            catch (AppException exception)
            {
                return exception.Error.Type;
            }
        })));

        outcomes.Should().Contain((AppErrorType?)null, "one hash takes the slot");
        outcomes.Should().Contain(AppErrorType.TooManyRequests, "the others find it taken and do not wait");
    }

    [Fact]
    public void Hash_ShouldQueueForASlot_AndStillVerify()
    {
        var hasher = new Argon2PasswordHasher<object>(maxConcurrentHashes: 2, queueTimeout: TimeSpan.FromSeconds(30));

        var hashes = Enumerable.Range(0, 4).AsParallel().Select(index => hasher.HashPassword(new object(), "pw-" + index)).ToList();

        hashes.Select((hash, index) => hasher.VerifyHashedPassword(new object(), hash, "pw-" + index))
            .Should().AllBeEquivalentTo(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Defaults_ShouldBoundTheHashesAndSpaceTheResends()
    {
        Argon2PasswordHasher<object>.DefaultMaxConcurrentHashes.Should().BeInRange(2, 4);
        var otp = new OtpOptions();
        otp.RateLimitWindow.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(30)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        otp.MaxSubjectFailures.Should().Be(10);
        otp.SubjectFailureWindow.Should().Be(TimeSpan.FromHours(1));
    }
}

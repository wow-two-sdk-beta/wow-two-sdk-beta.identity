using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>
/// The purpose-token key and the one-time-code hash key take standard base64 and base64url, padded or not, because a
/// deploy tool writes either; both still need 32 decoded bytes, and both spellings of one key act as one key.
/// </summary>
public sealed class SecretKeyEncodingTests
{
    /// <summary>Bytes whose base64 uses both <c>+</c> and <c>/</c>, so the two alphabets spell the key differently.</summary>
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(index => (byte)(0xFB - (index * 7))).ToArray();

    [Fact]
    public void Spellings_ShouldDifferInTheAlphabet_SoTheTestsMeanSomething()
    {
        Standard(Key).Should().ContainAny("+", "/");
        Url(Key).Should().NotContainAny("+", "/", "=");
        Standard(Key).Should().NotBe(Url(Key));
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void TryDecode_ShouldReadEverySpellingOfOneKey(string spelling)
    {
        SecretKeyEncoding.TryDecode(spelling, out var bytes).Should().BeTrue();

        bytes.Should().Equal(Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all!")]
    [InlineData("A")]
    [InlineData("AAAAA")]
    public void TryDecode_ShouldRefuseAnUnsetOrMalformedKey(string? spelling)
    {
        SecretKeyEncoding.TryDecode(spelling, out var bytes).Should().BeFalse();

        bytes.Should().BeEmpty();
        SecretKeyEncoding.DecodedLength(spelling).Should().Be(0);
    }

    [Fact]
    public void TryDecode_ShouldRefuseAKeyMixingTheTwoAlphabets()
    {
        var mixed = Standard(Key).Replace("+", "-", StringComparison.Ordinal);

        SecretKeyEncoding.TryDecode(mixed + "/", out _).Should().BeFalse();
    }

    [Fact]
    public void Decode_ShouldNameTheSetting_NeverTheKey()
    {
        var act = () => SecretKeyEncoding.Decode("not base64 at all!", "Identity:Otp:HashKey");

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("Identity:Otp:HashKey must be base64 or base64url.");
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void TokenKey_ShouldTakeEverySpelling_AndSignAsOneKey(string spelling)
    {
        var reference = Issuer(Standard(Key));
        var issuer = Issuer(spelling);
        var user = new IdentityUser { Id = Guid.Parse("11111111-2222-3333-4444-555555555555"), SecurityStamp = "stamp" };

        var token = issuer.Issue(user, "Invite");

        reference.Verify(user, "Invite", token).Should().BeTrue("a base64 key and its base64url spelling are one key");
    }

    [Fact]
    public void TokenKey_ShouldStopTheStart_WhenShorterThan32Bytes()
    {
        using var provider = TokenProvider(Url(Key[..31]));

        FluentActions.Invoking(() => provider.GetRequiredService<UserTokenOptions>()).Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void TokenKey_ShouldStopTheStart_WhenItIsNotBase64()
    {
        using var provider = TokenProvider("%%%%");

        FluentActions.Invoking(() => provider.GetRequiredService<UserTokenOptions>()).Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void HashKey_ShouldTakeEverySpelling_AndKeyOneDigest(string spelling)
    {
        var reference = new OtpCodeHasher(new OtpOptions { HashKey = Standard(Key) });
        var hasher = new OtpCodeHasher(new OtpOptions { HashKey = spelling });

        hasher.IsKeyConfigured.Should().BeTrue();
        hasher.Subject("+998901234567").Should().Be(reference.Subject("+998901234567"));
        hasher.Code("subject", "scope", "123456").Should().Be(reference.Code("subject", "scope", "123456"));
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void HashKey_ShouldPassTheStartCheck_InEverySpelling(string spelling)
    {
        using var provider = new ServiceCollection().AddOtpService(o => o.HashKey = spelling).BuildServiceProvider();

        provider.GetRequiredService<OtpOptions>().HashKey.Should().Be(spelling);
    }

    [Fact]
    public void HashKey_ShouldStopTheStart_WhenShorterThan32Bytes_InEitherSpelling()
    {
        foreach (var short31 in new[] { Standard(Key[..31]), Url(Key[..31]) })
        {
            using var provider = new ServiceCollection().AddOtpService(o => o.HashKey = short31).BuildServiceProvider();

            FluentActions.Invoking(() => provider.GetRequiredService<OtpOptions>()).Should().Throw<OptionsValidationException>();
        }
    }

    [Fact]
    public void HashKey_ShouldStopTheStart_WhenItIsNotBase64()
    {
        using var provider = new ServiceCollection().AddOtpService(o => o.HashKey = "%%%%").BuildServiceProvider();

        FluentActions.Invoking(() => provider.GetRequiredService<OtpOptions>()).Should().Throw<OptionsValidationException>();
    }

    /// <summary>Gets one key in every spelling: standard padded, standard unpadded, base64url padded and base64url unpadded.</summary>
    public static TheoryData<string> Spellings() =>
        [Standard(Key), Standard(Key).TrimEnd('='), Standard(Key).Replace('+', '-').Replace('/', '_'), Url(Key), $" {Url(Key)}\n"];

    private static string Standard(byte[] bytes) => Convert.ToBase64String(bytes);

    private static string Url(byte[] bytes) => Standard(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static UserTokenIssuer Issuer(string key)
        => new(new UserTokenOptions { SigningKey = key }, TimeProvider.System);

    private static ServiceProvider TokenProvider(string key)
    {
        var services = new ServiceCollection();
        new IdentityBuilder(services).AddUserTokens(options => options.SigningKey = key);
        return services.BuildServiceProvider();
    }
}

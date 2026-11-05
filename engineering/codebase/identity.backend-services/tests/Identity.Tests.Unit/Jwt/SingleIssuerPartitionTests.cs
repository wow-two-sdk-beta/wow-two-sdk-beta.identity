using System.Net;
using AwesomeAssertions;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>One issuer keeps working as it did, except that a token never names its own tenant.</summary>
public sealed class SingleIssuerPartitionTests
{
    private const string Issuer = "https://one-issuer.example.test/";
    private const string Audience = "https://api.example.test/";
    private const string Key = "a-single-issuer-test-key-of-32-bytes-at-least";

    [Fact]
    public async Task Token_ShouldNotChooseItsOwnPartition()
    {
        var token = SignedTokens.Sign(
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(Key)),
            Issuer,
            Audience,
            new Dictionary<string, object> { ["sub"] = "caller", ["partition"] = "someone-elses" },
            SecurityAlgorithms.HmacSha256);
        await using var host = await ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Issuer = Issuer;
            options.Audience = Audience;
            options.SymmetricKey = Key;
        }));

        (await host.StatusAsync(token)).Should().Be(HttpStatusCode.OK);
        var claims = await host.ClaimsAsync(token);
        claims["sub"].Should().Equal("caller");
        claims.Contains("partition").Should().BeFalse();
    }
}

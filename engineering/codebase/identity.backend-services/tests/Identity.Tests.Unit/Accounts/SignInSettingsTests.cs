using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>The password, sign-in and lockout rules bind from <c>Identity:*</c>, beat the code's own, and are checked when the host starts.</summary>
public sealed class SignInSettingsTests
{
    [Fact]
    public void Options_ShouldKeepTheirDefaults_WhenNoSettingsExist()
    {
        using var provider = Build([]);

        var options = provider.GetRequiredService<IdentityCoreOptions>();

        options.Password.MinLength.Should().Be(15, "decision Q20");
        options.Password.MaxLength.Should().Be(128);
        options.Password.Change.Confirmation.Should().Be(PasswordChangeConfirmationType.None);
        options.Password.Change.IsSignInAgainRequired.Should().BeFalse();
        options.Password.Change.Lifetime.Should().Be(TimeSpan.FromMinutes(15));
        options.SignIn.IsConfirmedEmailRequired.Should().BeFalse();
        options.Lockout.MaxFailedAttempts.Should().Be(5);
    }

    [Fact]
    public void Options_ShouldBindFromTheIdentitySections_AndBeatTheCode()
    {
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["Identity:Password:MinLength"] = "20",
                ["Identity:Password:IsDigitRequired"] = "true",
                ["Identity:Password:IsAccountIdentifierRejected"] = "false",
                ["Identity:Password:Change:Confirmation"] = "Code",
                ["Identity:Password:Change:IsSignInAgainRequired"] = "true",
                ["Identity:Password:Change:Lifetime"] = "00:10:00",
                ["Identity:SignIn:IsConfirmedEmailRequired"] = "true",
                ["Identity:SignIn:IsEmailIdentifierAllowed"] = "false",
                ["Identity:Lockout:MaxFailedAttempts"] = "3",
                ["Identity:Lockout:DefaultLockout"] = "00:30:00",
                ["Identity:Lockout:IsEnabledForNewUsers"] = "false",
            },
            options =>
            {
                options.Password.MinLength = 9;
                options.Password.IsUppercaseRequired = true;
            });

        var options = provider.GetRequiredService<IdentityCoreOptions>();

        options.Password.MinLength.Should().Be(20, "the host section is applied last");
        options.Password.IsDigitRequired.Should().BeTrue();
        options.Password.IsAccountIdentifierRejected.Should().BeFalse();
        options.Password.IsUppercaseRequired.Should().BeTrue("a setting the host leaves out keeps the code's value");
        options.Password.Change.Confirmation.Should().Be(PasswordChangeConfirmationType.Code);
        options.Password.Change.IsSignInAgainRequired.Should().BeTrue();
        options.Password.Change.Lifetime.Should().Be(TimeSpan.FromMinutes(10));
        options.SignIn.IsConfirmedEmailRequired.Should().BeTrue();
        options.SignIn.IsEmailIdentifierAllowed.Should().BeFalse();
        options.Lockout.MaxFailedAttempts.Should().Be(3);
        options.Lockout.DefaultLockout.Should().Be(TimeSpan.FromMinutes(30));
        options.Lockout.IsEnabledForNewUsers.Should().BeFalse();
    }

    [Theory]
    [InlineData("Identity:Password:MinLength", "0", "MinLength must be positive")]
    [InlineData("Identity:Password:MaxLength", "10", "MaxLength must not be below MinLength")]
    [InlineData("Identity:Password:RequiredUniqueChars", "500", "RequiredUniqueChars")]
    [InlineData("Identity:Password:Change:Lifetime", "00:00:00", "Identity:Password:Change:Lifetime")]
    [InlineData("Identity:Password:Change:Confirmation", "Link", "LinkTemplate")]
    [InlineData("Identity:Lockout:MaxFailedAttempts", "0", "MaxFailedAttempts")]
    [InlineData("Identity:Lockout:DefaultLockout", "00:00:00", "DefaultLockout")]
    public void Options_ShouldRefuseTheStart_WhenASettingIsInvalidOnItsOwn(string key, string value, string expected)
    {
        using var provider = Build(new Dictionary<string, string?> { [key] = value });

        var act = () => provider.GetRequiredService<IdentityCoreOptions>();

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void Options_ShouldAcceptLinkConfirmation_WhenATemplateCarriesTheToken()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Password:Change:Confirmation"] = "Link",
            ["Identity:Password:Change:LinkTemplate"] = "https://id.example/change-password#token={token}",
        });

        provider.GetRequiredService<IdentityCoreOptions>().Password.Change.Confirmation.Should().Be(PasswordChangeConfirmationType.Link);
    }

    [Fact]
    public void Options_ShouldRefuseTheStart_WhenTheConfirmationIsNotOneOfTheThree()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Sms" });

        var act = () => provider.GetRequiredService<IdentityCoreOptions>();

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Options_ShouldRefuseTheStart_WhenALinkTemplateLacksTheToken()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Password:Change:Confirmation"] = "Link",
            ["Identity:Password:Change:LinkTemplate"] = "https://id.example/change-password",
        });

        var act = () => provider.GetRequiredService<IdentityCoreOptions>();

        act.Should().Throw<OptionsValidationException>().WithMessage("*{token}*");
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, Action<IdentityCoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddUserAccounts(configure);
        return services.BuildServiceProvider();
    }
}

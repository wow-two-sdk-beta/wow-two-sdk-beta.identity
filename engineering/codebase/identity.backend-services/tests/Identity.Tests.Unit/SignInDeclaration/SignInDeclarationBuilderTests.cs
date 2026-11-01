using AwesomeAssertions;
using Microsoft.Net.Http.Headers;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.SignInDeclaration;

/// <summary>
/// The declaration built from its settings: each method only while it is on, in order, its fields named as the requests
/// name them, fe sdk rule codes with their operands, and the server's own message for each password rule, per locale.
/// </summary>
public sealed class SignInDeclarationBuilderTests
{
    private static SignInDeclarationInput Input(PasswordOptions? password = null, IReadOnlyList<string>? countries = null) => new()
    {
        BrandName = "Ocharo",
        Theme = "platform",
        Password = password,
        PhoneCountries = countries,
    };

    [Fact]
    public void Build_ShouldListPasswordThenPhoneCode_WhenBothAreOn()
    {
        var declaration = SignInDeclarationBuilder.Build(Input(new PasswordOptions(), ["uz"]), "en");

        declaration.Methods.Select(method => method.Id).Should().Equal("password", "phone-code");
        declaration.Locale.Should().Be("en");
        declaration.Locales.Should().Equal("uz", "ru", "en");

        var password = declaration.Methods[0];
        password.Channels.Should().BeEmpty();
        password.Fields.Select(field => (field.Name, field.Kind)).Should().Equal(("identifier", "text"), ("password", "password"));
        password.Fields[1].Rules.Select(rule => rule.Code).Should().Equal(new[] { "required" }, "a stored password the rules now refuse still signs in once, to its change");
        password.Fields[0].Rules.Single().Message.Should().Be("Enter your user name or email.");

        var phone = declaration.Methods[1];
        phone.Channels.Should().Equal("telegram");
        phone.Fields.Select(field => (field.Name, field.Kind)).Should().Equal(("phone", "phone"), ("code", "pin"));
        phone.Fields[0].Countries.Should().Equal("UZ");
        phone.PasswordRules.Should().BeNull();
        var length = phone.Fields[1].Rules.Single(rule => rule.Code == "length");
        length.Params.Should().BeEquivalentTo(new Dictionary<string, object> { ["length"] = 6, ["unit"] = "characters" });
        length.Message.Should().Be("The code has 6 characters.");
    }

    [Fact]
    public void Build_ShouldLeaveAMethodOut_WhileItIsOff()
    {
        SignInDeclarationBuilder.Build(Input(), "en").Methods.Should().BeEmpty();
        SignInDeclarationBuilder.Build(Input(countries: []), "en").Methods.Single().Id.Should().Be("phone-code");
        SignInDeclarationBuilder.Build(Input(new PasswordOptions()), "en").Methods.Single().Id.Should().Be("password");
    }

    [Fact]
    public void Build_ShouldAllowEveryCountry_WhenNoneIsListed_AndAskForAUserNameOnly_WhenEmailsDoNotSignIn()
    {
        var declaration = SignInDeclarationBuilder.Build(Input(new PasswordOptions(), []) with { IsEmailIdentifierAllowed = false, PhoneCodeLength = 8 }, "en");

        declaration.Methods[0].Fields[0].Rules.Single().Message.Should().Be("Enter your user name.");
        declaration.Methods[1].Fields[0].Countries.Should().BeEmpty();
        declaration.Methods[1].Fields[1].Rules.Single(rule => rule.Code == "length").Params!["length"].Should().Be(8);
    }

    [Fact]
    public void Build_ShouldCarryTheLookSignUpAndVerifications()
    {
        var declaration = SignInDeclarationBuilder.Build(
            Input() with
            {
                Theme = "ocharo",
                BackgroundUrl = "/backgrounds/ocharo.jpg",
                IsSignUpEnabled = true,
                IsConfirmedPhoneNumberRequired = true,
            },
            "en");

        declaration.BrandName.Should().Be("Ocharo");
        declaration.Theme.Should().Be("ocharo");
        declaration.BackgroundUrl.Should().Be("/backgrounds/ocharo.jpg");
        declaration.IsSignUpEnabled.Should().BeTrue();
        declaration.Requirements.Should().Be(new SignInRequirementsDto { IsConfirmedPhoneNumberRequired = true });
    }

    [Fact]
    public void PasswordRules_ShouldHoldOnlyTheBoundsAndIdentifiers_ByDefault()
    {
        var rules = SignInDeclarationBuilder.PasswordRules(new PasswordOptions(), "en").ToList();

        rules.Select(rule => rule.Code).Should().Equal("min", "max", "notEqual");
        rules[0].Params.Should().BeEquivalentTo(new Dictionary<string, object> { ["min"] = 15, ["unit"] = "characters" });
        rules[1].Params.Should().BeEquivalentTo(new Dictionary<string, object> { ["max"] = 128, ["unit"] = "characters" });
        rules[2].Params!["fields"].Should().BeEquivalentTo(new[] { "userName", "email" });
        rules[2].Params!["isCaseIgnored"].Should().Be(true);
    }

    [Fact]
    public void PasswordRules_ShouldSayWhatTheServerSays_ForEveryRule()
    {
        var options = new IdentityCoreOptions();
        var rules = options.Password;
        rules.MinLength = 16;
        rules.MaxLength = 20;
        rules.IsDigitRequired = true;
        rules.IsLowercaseRequired = true;
        rules.IsUppercaseRequired = true;
        rules.IsNonAlphanumericRequired = true;
        rules.RequiredUniqueChars = 5;
        var user = new IdentityUser { UserName = "alice", Email = "alice@example.test" };
        var validator = new PasswordRulesValidator(options);
        var server = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var password in new[] { "aaaa", "AAAA", new string('x', 21), "alice" })
        {
            foreach (var error in validator.ValidateAsync(user, password).GetAwaiter().GetResult())
                server[error.Code] = error.Description;
        }

        var declared = SignInDeclarationBuilder.PasswordRules(rules, "en").ToList();

        declared.Select(rule => rule.Code).Should().Equal("min", "max", "pattern", "pattern", "pattern", "pattern", "custom", "notEqual");
        declared.Select(rule => rule.Message).Should().Equal(
            server[IdentityErrorCodeConstants.PasswordTooShort],
            server[IdentityErrorCodeConstants.PasswordTooLong],
            server[IdentityErrorCodeConstants.PasswordRequiresDigit],
            server[IdentityErrorCodeConstants.PasswordRequiresLower],
            server[IdentityErrorCodeConstants.PasswordRequiresUpper],
            server[IdentityErrorCodeConstants.PasswordRequiresNonAlphanumeric],
            server[IdentityErrorCodeConstants.PasswordRequiresUniqueChars],
            server[IdentityErrorCodeConstants.PasswordMatchesAccount]);
        declared.Where(rule => rule.Code == "pattern").Select(rule => rule.Params!["pattern"]).Should().Equal("[0-9]", @"\p{Ll}", @"\p{Lu}", @"[^\p{L}\p{Nd}]");
        declared[6].Params.Should().BeEquivalentTo(new Dictionary<string, object> { ["rule"] = "distinctCharacters", ["min"] = 5 });
    }

    [Fact]
    public void Messages_ShouldTakeTheServersRuAndUzTexts_ForPasswordRules_AndFallBackToEnForTheRest()
    {
        var ru = SignInDeclarationBuilder.Build(Input(new PasswordOptions(), ["UZ"]), "ru");
        var uz = SignInDeclarationBuilder.Build(Input(new PasswordOptions(), ["UZ"]), "uz");

        ru.Locale.Should().Be("ru");
        ru.Methods[0].PasswordRules![0].Message.Should().Be("Пароль должен содержать не менее 15 символов.");
        uz.Methods[0].PasswordRules![0].Message.Should().Be("Parol kamida 15 ta belgidan iborat boʻlishi kerak.");
        ru.Methods[0].Fields[0].Rules.Single().Message.Should().Be("Enter your user name or email.", "the ru texts of the new keys are not written yet");
        uz.Methods[1].Fields[1].Rules[1].Message.Should().Be("The code has 6 characters.");
    }

    [Fact]
    public void Texts_ShouldExistInEn_ForEveryKeyAndPasswordRule()
    {
        var en = SignInDeclarationTextConstants.Messages["en"];

        en.Keys.Should().Contain(
        [
            SignInDeclarationTextConstants.IdentifierRequired, SignInDeclarationTextConstants.UserNameIdentifierRequired,
            SignInDeclarationTextConstants.PasswordRequired, SignInDeclarationTextConstants.PhoneRequired,
            SignInDeclarationTextConstants.CodeRequired, SignInDeclarationTextConstants.CodeLength,
        ]);
        SignInDeclarationTextConstants.Messages.Keys.Should().BeEquivalentTo("en", "ru", "uz");
    }

    [Theory]
    [InlineData("ru", null, "ru")]
    [InlineData("RU-ru", "uz", "ru")]
    [InlineData("de", "uz-Latn-UZ, en;q=0.5", "uz")]
    [InlineData(null, "de-DE, ru;q=0.4, uz;q=0.8", "uz")]
    [InlineData(null, "ru;q=0, en;q=0.1", "en")]
    [InlineData(null, "*", "en")]
    [InlineData("", "de", "en")]
    [InlineData(null, null, "en")]
    public void Locale_ShouldPreferTheQueryThenTheBrowsersBest_ThenEn(string? requested, string? acceptLanguage, string expected)
    {
        var ranges = acceptLanguage is null ? [] : StringWithQualityHeaderValue.ParseList([acceptLanguage]);

        SignInLocaleResolver.Resolve(requested, ranges).Should().Be(expected);
    }

    [Theory]
    [InlineData("platform", true)]
    [InlineData("ocharo", true)]
    [InlineData("dark-2", true)]
    [InlineData("Platform", false)]
    [InlineData("-x", false)]
    [InlineData("a--b", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ThemeName_ShouldBeARegistryId(string? theme, bool expected)
        => HostedPageLookRules.IsThemeName(theme).Should().Be(expected);

    [Theory]
    [InlineData("/backgrounds/ocharo.jpg", true)]
    [InlineData("https://cdn.example.test/bg.jpg", true)]
    [InlineData("//evil.test/bg.jpg", false)]
    [InlineData("/\\evil.test/bg.jpg", false)]
    [InlineData("http://cdn.example.test/bg.jpg", false)]
    [InlineData("https://user:pass@cdn.example.test/bg.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("backgrounds/bg.jpg", false)]
    [InlineData("/bg.jpg\")", false)]
    [InlineData("/a b.jpg", false)]
    public void BackgroundUrl_ShouldBeSameOriginOrHttps(string url, bool expected)
        => HostedPageLookRules.IsBackgroundUrl(url).Should().Be(expected);
}

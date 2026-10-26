using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Web.ErrorMapping;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.ErrorTranslation;

/// <summary>Identity's field errors translate through the ru/uz texts the module contributes to core's error translation.
/// Moved from the be sdk's <c>Web.Tests/ErrorTranslation/ErrorTranslationTests.cs</c>, where core held the texts.</summary>
public sealed class IdentityErrorTextTests
{
    [Fact]
    public void IdentityFailures_TranslateThroughTheModuleTextsWithTheirValues()
    {
        using var provider = Build(Enabled("en", "ru"), register: services => services.AddUserAccounts<IdentityUser>());
        var result = IdentityResult.Failed(
            new IdentityError { Code = IdentityErrorCodeConstants.PasswordTooShort, Description = "Passwords must be at least 12 characters.", Params = new Dictionary<string, object> { ["MinLength"] = 12 } },
            new IdentityError { Code = IdentityErrorCodeConstants.DuplicateEmail, Description = "Email 'a@b.uz' is already taken.", Params = new Dictionary<string, object> { ["Email"] = "a@b.uz" } });

        var errors = Errors(Render(provider, result.ToValidationError(), "ru"));

        errors.Select(e => (e.Property, e.Message)).Should().Equal(
            ("password", "Пароль должен содержать не менее 12 символов."),
            ("email", "Email a@b.uz уже используется."));
        Errors(Render(provider, result.ToValidationError(), "en")).Select(e => e.Message)
            .Should().Equal("Passwords must be at least 12 characters.", "Email 'a@b.uz' is already taken.");
    }

    private static Dictionary<string, string?> Enabled(params string[] cultures)
    {
        var settings = new Dictionary<string, string?> { ["Validation:Translation:Enabled"] = "true" };
        for (var index = 0; index < cultures.Length; index++)
            settings[$"Validation:Translation:SupportedCultures:{index}"] = cultures[index];
        return settings;
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, Action<ValidationOptions>? configure = null, Action<IServiceCollection>? register = null)
        => Build(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), configure, register);

    private static ServiceProvider Build(IConfigurationRoot configuration, Action<ValidationOptions>? configure = null, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.ConfigureValidation(configure);
        services.AddErrorHttpStatusMapping();
        services.AddSingleton<IAppErrorProblemDetailsFactory, AppErrorProblemDetailsFactory>();
        register?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static Microsoft.AspNetCore.Mvc.ProblemDetails Render(ServiceProvider provider, AppError error, string acceptLanguage, string? feature = null)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers.AcceptLanguage = acceptLanguage;
        if (feature is not null)
            context.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(new RequestCulture(feature), provider: null));

        return provider.GetRequiredService<IAppErrorProblemDetailsFactory>().Create(error, context);
    }

    private static IReadOnlyList<FieldError> Errors(Microsoft.AspNetCore.Mvc.ProblemDetails problem)
        => (IReadOnlyList<FieldError>)problem.Extensions["errors"]!;
}

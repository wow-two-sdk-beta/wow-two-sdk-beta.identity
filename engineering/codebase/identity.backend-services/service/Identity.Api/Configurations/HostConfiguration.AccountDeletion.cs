using System.Security.Claims;
using Identity.Api.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    private const string AccountDeletionPolicy = "identity-service-account-delete";
    private const string AccountDeletionScope = "accounts.delete";
    private const string AccountDeletionService = "identity-service-personal-data";

    private static WebApplicationBuilder AddAccountDeletion(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatedSettings<AccountDeletionSettings>(builder.Configuration, AccountDeletionSettings.SectionName,
            rules => rules.ValidateSetting(nameof(AccountDeletionSettings.Audience), "deletion-client",
                ValidDeletionClient, "Account deletion requires a client ID and a dedicated HTTPS audience."));
        builder.Services.AddKeyedScoped<UserPersonalDataService>(AccountDeletionService);
        builder.Services.AddOpenIddict().AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
            options.EnableTokenEntryValidation();
        });
        builder.Services.AddOptions<OpenIddictValidationOptions>().Configure<AccountDeletionSettings>((options, settings) =>
        {
            if (settings.Audience is { } audience)
                options.Audiences.Add(audience.AbsoluteUri);
        });
        builder.Services.AddOptions<AuthorizationOptions>().Configure<AccountDeletionSettings>((options, settings) =>
            options.AddPolicy(AccountDeletionPolicy, policy => policy
                .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => settings.ClientId is { } clientId && CanDeleteAccount(context.User, clientId))));
        return builder;
    }

    private static bool ValidDeletionClient(AccountDeletionSettings settings)
    {
        if (settings.ClientId is null && settings.Audience is null)
            return true;

        return !string.IsNullOrWhiteSpace(settings.ClientId) && settings.Audience is { IsAbsoluteUri: true } audience
            && audience.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(audience.UserInfo)
            && string.IsNullOrEmpty(audience.Fragment) && string.IsNullOrEmpty(audience.Query);
    }

    private static bool CanDeleteAccount(ClaimsPrincipal principal, string clientId)
    {
        return principal.HasScope(AccountDeletionScope)
            && principal.GetClaim(OpenIddictConstants.Claims.Subject) == clientId
            && principal.GetClaim(OpenIddictConstants.Claims.ClientId) == clientId
            && principal.FindFirst("sid") is null && principal.FindFirst("act") is null;
    }

    private static WebApplication MapAccountDeletion(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<AccountDeletionSettings>>().Value.Audience is null)
            return app;

        app.MapDelete("/api/service/people/{personId:guid}", DeleteAccountForServiceAsync)
            .RequireAuthorization(AccountDeletionPolicy);
        return app;
    }

    private static async Task<IResult> DeleteAccountForServiceAsync(Guid personId, UserAccountService accounts,
        [FromKeyedServices(AccountDeletionService)] UserPersonalDataService personalData, CancellationToken cancellationToken)
    {
        if (await accounts.FindByIdAsync(personId, cancellationToken) is { } person)
            await personalData.DeleteAsync(person, cancellationToken);

        return Results.NoContent();
    }
}

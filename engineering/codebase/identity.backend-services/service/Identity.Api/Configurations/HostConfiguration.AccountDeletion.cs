using System.Security.Claims;
using Identity.Application.AccountDeletion.Commands;
using Identity.Application.AccountDeletion.Models;
using Identity.Application.AccountDeletion.Repositories;
using Identity.Infrastructure.AccountDeletion.Constants;
using Identity.Infrastructure.AccountDeletion.Services;
using Identity.Infrastructure.AccountDeletion.Settings;
using Identity.Persistence.AccountDeletion.Repositories;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;
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

    /// <summary>
    /// Hosts <c>DELETE /api/service/people/{personId}</c> for the products' deletion clients: a client-credentials token whose
    /// audience is <c>Identity:AccountDeletion:Audience</c>, whose scope is <c>accounts.delete</c> and whose client is a key of
    /// <c>Identity:AccountDeletion:Clients</c>, each naming the sign-in clients its product owns in <c>ProductClientIds</c>.
    /// </summary>
    /// <remarks>
    ///   - no audience maps no route; a client not listed answers <c>403</c>
    ///   - <c>404</c> when the person never authorized a client of the caller's product; <c>409</c> when the person holds an
    ///     organization role or an instance role; <c>200</c> <c>{ "erased": false }</c> when the person also uses another
    ///     product's client (the caller's authorizations and tokens are revoked, the person stays); otherwise <c>204</c>, also
    ///     for a retry of an erased person
    ///   - every call writes a sign-in log row (<see cref="ServiceDeletionAuditConstants"/>)
    /// </remarks>
    private static WebApplicationBuilder AddAccountDeletion(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatedSettings<AccountDeletionSettings>(builder.Configuration, AccountDeletionSettings.SectionName,
            rules => rules.ValidateSetting(nameof(AccountDeletionSettings.Audience), "deletion-client",
                ValidDeletionClients, "Account deletion requires a dedicated HTTPS audience and, for each deletion client, the product clients it owns."));
        builder.Services.AddKeyedScoped<UserPersonalDataService>(ServiceAccountDeletionService.PersonalDataKey);
        builder.Services.AddScoped<ServiceAccountDeletionService>();
        builder.Services.AddScoped<IAccountAccessRepository, AccountAccessRepository>();
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
                .RequireAssertion(context => CanDeleteAccount(context.User, settings))));
        return builder;
    }

    private static bool ValidDeletionClients(AccountDeletionSettings settings)
    {
        if (settings.Audience is null)
            return settings.Clients.Count == 0;

        return settings.Audience is { IsAbsoluteUri: true } audience
            && audience.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(audience.UserInfo)
            && string.IsNullOrEmpty(audience.Fragment) && string.IsNullOrEmpty(audience.Query)
            && settings.Clients.Count > 0
            && settings.Clients.All(pair => !string.IsNullOrWhiteSpace(pair.Key)
                && pair.Value is { ProductClientIds.Length: > 0 }
                && pair.Value.ProductClientIds.All(id => !string.IsNullOrWhiteSpace(id)));
    }

    private static bool CanDeleteAccount(ClaimsPrincipal principal, AccountDeletionSettings settings)
    {
        return principal.HasScope(AccountDeletionScope)
            && principal.GetClaim(OpenIddictConstants.Claims.ClientId) is { Length: > 0 } clientId
            && settings.Clients.ContainsKey(clientId)
            && principal.GetClaim(OpenIddictConstants.Claims.Subject) == clientId
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

    private static async Task<IResult> DeleteAccountForServiceAsync(Guid personId, ClaimsPrincipal principal,
        ISender sender, CancellationToken cancellationToken)
    {
        var caller = principal.GetClaim(OpenIddictConstants.Claims.ClientId)!;
        var result = await sender.SendAsync(new DeleteServiceAccountCommand(caller, personId), cancellationToken);

        return result.Match<IResult>(success => success.Data switch
        {
            ServiceDeletionOutcome.NotThisProductsPerson => DeletionProblem(StatusCodes.Status404NotFound, "No such person for this client's product.", "PersonNotFound"),
            ServiceDeletionOutcome.HoldsAccess => DeletionProblem(StatusCodes.Status409Conflict, "The person holds an organization role or a console permission.", "PersonHoldsAccess"),
            ServiceDeletionOutcome.Kept => Results.Ok(new { erased = false }),
            _ => Results.NoContent(),
        }, failure => throw failure.Error.ToException());
    }

    private static IResult DeletionProblem(int status, string detail, string code)
        => Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["identityCode"] = code });
}

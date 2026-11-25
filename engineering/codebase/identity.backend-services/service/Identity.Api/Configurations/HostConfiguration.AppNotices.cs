using System.Security.Claims;
using Identity.Application.Notices.Commands;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using Identity.Api.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Notices;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    private const string AppNoticePolicy = "identity-app-notices";

    /// <summary>
    /// Hosts app notices: <c>POST /api/identity/v1/notices</c> for services calling with a client-credentials token whose
    /// audience is <c>Identity:Notices:Audience</c> and whose scopes hold <c>identity.notices.send</c>, granted per client
    /// under <c>Identity:Server:Clients</c>. The Module sends each notice through the bot as the token's client.
    /// </summary>
    /// <remarks>
    ///   - no audience maps no route; the limits come from the Module's <c>Identity:Notices</c> keys
    ///   - a person's token (it carries <c>sid</c> or <c>act</c>) or a token without the scope answers <c>403</c>
    ///   - <c>202</c> sent, <c>200</c> muted, <c>404</c> not linked, <c>400</c>, <c>409</c>, <c>429</c>, <c>502</c> and
    ///     <c>503</c> problems carry their code in <c>identityCode</c>, a retry's wait in <c>Retry-After</c>
    /// </remarks>
    private static WebApplicationBuilder AddAppNoticeRoute(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatedSettings<AppNoticeSettings>(builder.Configuration, AppNoticeSettings.SectionName,
            rules => rules.ValidateSetting(nameof(AppNoticeSettings.Audience), "https-resource",
                settings => settings.Audience is null || IsResourceUri(settings.Audience),
                "The app notice route's audience must be an absolute HTTPS resource URI."));
        new IdentityBuilder(builder.Services).AddAppNotices();
        builder.Services.AddOpenIddict().AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
            options.EnableTokenEntryValidation();
        });
        builder.Services.AddOptions<OpenIddictValidationOptions>().Configure<IOptions<AppNoticeSettings>>((options, settings) =>
        {
            if (settings.Value.Audience is { } audience)
                options.Audiences.Add(audience.AbsoluteUri);
        });
        builder.Services.AddOptions<AuthorizationOptions>().Configure<IOptions<AppNoticeSettings>>((options, settings) =>
            options.AddPolicy(AppNoticePolicy, policy => policy
                .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => settings.Value.Audience is { } audience && CanSendNotices(context.User, audience))));
        return builder;
    }

    private static bool IsResourceUri(Uri audience)
        => audience.IsAbsoluteUri && audience.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(audience.UserInfo)
            && string.IsNullOrEmpty(audience.Fragment) && string.IsNullOrEmpty(audience.Query);

    /// <summary>A service's own token for the route's audience with the scope: never a person's, nor one acting for a person.</summary>
    private static bool CanSendNotices(ClaimsPrincipal principal, Uri audience)
        => principal.HasScope(AppNoticeConstants.Scope)
            && principal.HasAudience(audience.AbsoluteUri)
            && principal.GetClaim(OpenIddictConstants.Claims.ClientId) is { Length: > 0 and <= AppNoticeOptions.MaxClientIdLength } clientId
            && principal.GetClaim(OpenIddictConstants.Claims.Subject) == clientId
            && principal.FindFirst("sid") is null && principal.FindFirst("act") is null;

    private static WebApplication MapAppNotices(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<AppNoticeSettings>>().Value.Audience is null)
            return app;

        app.MapPost(AppNoticeConstants.Path, SendAppNoticeAsync).RequireAuthorization(AppNoticePolicy);
        return app;
    }

    private static async Task<IResult> SendAppNoticeAsync(AppNotice notice, ClaimsPrincipal principal, ISender sender, HttpContext http, CancellationToken cancellationToken)
    {
        var command = new SendAppNoticeCommand(principal.GetClaim(OpenIddictConstants.Claims.ClientId)!, notice);
        var result = await sender.SendAsync(command, cancellationToken);
        if (result is AppResult<AppNoticeDelivery>.Failure failure)
            return NoticeProblem(failure.Error, http);

        var delivery = ((AppResult<AppNoticeDelivery>.Success)result).Data;
        return delivery.Status switch
        {
            AppNoticeStatusConstants.Sent => Results.Json(ApiResponse<AppNoticeDelivery>.Ok(delivery), statusCode: StatusCodes.Status202Accepted),
            AppNoticeStatusConstants.Muted => Results.Ok(ApiResponse<AppNoticeDelivery>.Ok(delivery)),
            _ => Results.Problem(
                detail: "The person has no confirmed phone linked to the instance's Telegram bot.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?>
                {
                    [AppNoticeConstants.CodeMetadataKey] = AppNoticeErrorCodeConstants.NotLinked,
                    ["status"] = AppNoticeStatusConstants.NotLinked,
                }),
        };
    }

    /// <summary>A failure as a problem: its status by code, <c>identityCode</c>, Telegram's <c>reason</c>, and <c>Retry-After</c>.</summary>
    private static IResult NoticeProblem(AppError error, HttpContext http)
    {
        var metadata = error.Metadata ?? new Dictionary<string, object?>();
        var code = metadata.TryGetValue(AppNoticeConstants.CodeMetadataKey, out var value) ? value as string : null;
        var status = code switch
        {
            AppNoticeErrorCodeConstants.Invalid => StatusCodes.Status400BadRequest,
            AppNoticeErrorCodeConstants.IdempotencyKeyReused or AppNoticeErrorCodeConstants.InProgress => StatusCodes.Status409Conflict,
            AppNoticeErrorCodeConstants.RateLimited => StatusCodes.Status429TooManyRequests,
            AppNoticeErrorCodeConstants.Refused => StatusCodes.Status502BadGateway,
            AppNoticeErrorCodeConstants.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };
        var extensions = new Dictionary<string, object?> { [AppNoticeConstants.CodeMetadataKey] = code };
        if (metadata.TryGetValue(AppNoticeConstants.ReasonMetadataKey, out var reason))
            extensions[AppNoticeConstants.ReasonMetadataKey] = reason;
        if (metadata.TryGetValue(AppNoticeConstants.RetryAfterMetadataKey, out var retryAfter) && retryAfter is string seconds)
            http.Response.Headers.RetryAfter = seconds;
        return Results.Problem(detail: error.Message, statusCode: status, extensions: extensions);
    }
}

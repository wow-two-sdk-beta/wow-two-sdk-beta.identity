using System.Security.Claims;
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
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Members;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    private const string MemberPolicy = "identity-members-read";

    /// <summary>
    /// Hosts organization members for services: <c>GET /api/identity/v1/organizations/{organizationId}/members[?role=]</c> for a
    /// client-credentials token whose audience is <c>Identity:Members:Audience</c> and whose scopes hold
    /// <c>identity.members.read</c>, granted per client under <c>Identity:Server:Clients</c>.
    /// </summary>
    /// <remarks>
    ///   - no audience maps no route
    ///   - a person's token (it carries <c>sid</c> or <c>act</c>) or a token without the scope answers <c>403</c>
    ///   - <c>200</c> the members (<c>sub</c>, <c>role</c>, <c>hasTelegramLink</c>); <c>404</c> <c>OrganizationNotFound</c> for an
    ///     organization the instance lacks or one a bound client (its token's <c>org_id</c>) is not bound to; <c>400</c>
    ///     <c>UnknownOrganizationRole</c> for a role outside the catalog
    /// </remarks>
    private static WebApplicationBuilder AddMemberRoute(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatedSettings<MemberSettings>(builder.Configuration, MemberSettings.SectionName,
            rules => rules.ValidateSetting(nameof(MemberSettings.Audience), "https-resource",
                settings => settings.Audience is null || IsResourceUri(settings.Audience),
                "The members route's audience must be an absolute HTTPS resource URI."));
        builder.Services.AddOpenIddict().AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
            options.EnableTokenEntryValidation();
        });
        builder.Services.AddOptions<OpenIddictValidationOptions>().Configure<IOptions<MemberSettings>>((options, settings) =>
        {
            if (settings.Value.Audience is { } audience)
                options.Audiences.Add(audience.AbsoluteUri);
        });
        builder.Services.AddOptions<AuthorizationOptions>().Configure<IOptions<MemberSettings>>((options, settings) =>
            options.AddPolicy(MemberPolicy, policy => policy
                .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => settings.Value.Audience is { } audience && CanReadMembers(context.User, audience))));
        return builder;
    }

    /// <summary>A service's own token for the route's audience with the scope: never a person's, nor one acting for a person.</summary>
    private static bool CanReadMembers(ClaimsPrincipal principal, Uri audience)
        => principal.HasScope(MemberConstants.Scope)
            && principal.HasAudience(audience.AbsoluteUri)
            && principal.GetClaim(OpenIddictConstants.Claims.ClientId) is { Length: > 0 } clientId
            && principal.GetClaim(OpenIddictConstants.Claims.Subject) == clientId
            && principal.FindFirst("sid") is null && principal.FindFirst("act") is null;

    private static WebApplication MapMembers(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<MemberSettings>>().Value.Audience is null)
            return app;

        app.MapGet(MemberConstants.Path.Replace("{organizationId}", "{organizationId:guid}", StringComparison.Ordinal), ListMembersAsync)
            .RequireAuthorization(MemberPolicy);
        return app;
    }

    private static async Task<IResult> ListMembersAsync(Guid organizationId, string? role, ClaimsPrincipal principal, IMemberBroker members, CancellationToken cancellationToken)
    {
        // A client bound to one organization reads only that one; another answers as an unknown one would.
        if (principal.GetClaim(OrganizationClaimTypeConstants.Id) is { Length: > 0 } bound
            && !(Guid.TryParse(bound, out var boundId) && boundId == organizationId))
        {
            return MemberProblem(StatusCodes.Status404NotFound, "No such organization.", IdentityErrorCodeConstants.OrganizationNotFound);
        }

        var result = await members.ListAsync(organizationId, role, cancellationToken);
        if (!result.IsFailure(out var error, out var listed))
            return Results.Ok(ApiResponse<IReadOnlyList<MemberModel>>.Ok(listed!));

        var code = error.Metadata?.TryGetValue(MemberConstants.CodeMetadataKey, out var value) == true ? value as string : null;
        return error.Type switch
        {
            AppErrorType.NotFound => MemberProblem(StatusCodes.Status404NotFound, error.Message, code),
            AppErrorType.Validation => MemberProblem(StatusCodes.Status400BadRequest, error.Message, code),
            _ => MemberProblem(StatusCodes.Status500InternalServerError, error.Message, code),
        };
    }

    private static IResult MemberProblem(int status, string detail, string? code)
        => Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { [MemberConstants.CodeMetadataKey] = code });
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Gates account routes on <see cref="SignInMethodOptions"/>.</summary>
internal static class SignInMethodEndpointConventionExtensions
{
    /// <summary>
    /// Answers <c>404</c> (<c>SignInMethodOff</c>) before the route runs while any of <paramref name="methods"/> is off;
    /// without <see cref="SignInMethodOptions"/> registered every method counts as on.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="methods"><see cref="SignInMethodOptions.PasswordMethod"/> or <see cref="SignInMethodOptions.EmailMethod"/>.</param>
    public static RouteHandlerBuilder RequireMethods(this RouteHandlerBuilder route, params string[] methods)
        => route.AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetService<IOptionsMonitor<SignInMethodOptions>>()?.CurrentValue;
            if (options is not null && methods.FirstOrDefault(method => !options.IsOn(method)) is { } off)
                throw InvitationErrors.Coded(AppErrorType.NotFound, $"Sign-in by {off} is off on this instance.", IdentityErrorCodeConstants.SignInMethodOff).ToException();

            return await next(context);
        });
}

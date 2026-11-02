using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Web.RateLimit;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Registers and applies the account API's credential rate limit (<see cref="AccountRateLimitPolicyConstants.Credentials"/>).</summary>
internal static class AccountRateLimitServiceCollectionExtensions
{
    /// <summary>
    /// Add the default <see cref="AccountRateLimitPolicyConstants.Credentials"/> policy to the rate limiter, once, unless the
    /// host declares one of that name under <c>RateLimits:Policies</c> (the be sdk then adds its own). A limiter left on the
    /// framework's 503 rejection answers 429 instead.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddAccountRateLimit(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AccountRateLimitMarker)))
            return services;

        services.AddSingleton<AccountRateLimitMarker>();
        services.AddRateLimiter(static _ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitSettings>>(static (options, settings) =>
        {
            if (options.RejectionStatusCode == StatusCodes.Status503ServiceUnavailable)
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            if (settings.Value.Policies.ContainsKey(AccountRateLimitPolicyConstants.Credentials))
                return;

            options.AddPolicy(AccountRateLimitPolicyConstants.Credentials, static context => RateLimitPartition.GetSlidingWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                static _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = AccountRateLimitPolicyConstants.DefaultPermitLimit,
                    Window = AccountRateLimitPolicyConstants.DefaultWindow,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                }));
        });
        return services;
    }

    /// <summary>
    /// Put <paramref name="route"/> under the credential policy when it is registered (<c>AddAccountEndpoints</c> or
    /// <c>AddPhoneSignIn</c>); a host that mapped the routes without it keeps them unlimited rather than failing each request.
    /// </summary>
    /// <param name="route">The mapped route.</param>
    /// <param name="endpoints">The route builder it was mapped on.</param>
    public static RouteHandlerBuilder LimitCredentials(this RouteHandlerBuilder route, IEndpointRouteBuilder endpoints)
        => endpoints.ServiceProvider.GetService<AccountRateLimitMarker>() is null
            ? route
            : route.RequireRateLimiting(AccountRateLimitPolicyConstants.Credentials);

    /// <summary>Marks a service collection whose credential policy is registered.</summary>
    internal sealed class AccountRateLimitMarker;
}

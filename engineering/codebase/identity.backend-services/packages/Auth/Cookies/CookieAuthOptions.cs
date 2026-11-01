using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace WoW.Two.Sdk.Beta.Be.Identity.Cookies;

/// <summary>Holds cookie authentication options.</summary>
public sealed record CookieAuthOptions
{
    /// <summary>Cookie name; <see cref="Environment"/> suffixes it outside <see cref="PrimaryEnvironment"/>.</summary>
    public string CookieName { get; set; } = ".app.auth";

    /// <summary>The deploy environment the session serves, such as <c>dev</c> (the runner's <c>DEPLOY_ENVIRONMENT</c>);
    /// <c>null</c> on a local run. Cookies ignore the port, so two environments on one host name would overwrite each
    /// other's session: any environment but <see cref="PrimaryEnvironment"/> adds its own name to
    /// <see cref="CookieName"/> (<see cref="EnvironmentCookieName.For"/>). Default <c>null</c>: the plain name.</summary>
    public string? Environment { get; set; }

    /// <summary>The environment that keeps the plain <see cref="CookieName"/>. Default
    /// <see cref="EnvironmentCookieName.DefaultPrimaryEnvironment"/> (<c>prod</c>).</summary>
    public string PrimaryEnvironment { get; set; } = EnvironmentCookieName.DefaultPrimaryEnvironment;

    /// <summary>Cookie expiration.</summary>
    public TimeSpan ExpireTimeSpan { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Use sliding expiration. Default <c>true</c>.</summary>
    public bool IsExpirationSliding { get; set; } = true;

    /// <summary>Sign-in path, where a challenge redirects in <see cref="AuthChallengeMode.Mvc"/>. Default <c>/api/identity/sign-in</c>.</summary>
    public PathString SignInPath { get; set; } = "/api/identity/sign-in";

    /// <summary>Sign-out path. Default <c>/api/identity/sign-out</c>.</summary>
    public PathString SignOutPath { get; set; } = "/api/identity/sign-out";

    /// <summary>Challenge style. Default <see cref="AuthChallengeMode.Mvc"/>.</summary>
    public AuthChallengeMode Mode { get; set; } = AuthChallengeMode.Mvc;
}

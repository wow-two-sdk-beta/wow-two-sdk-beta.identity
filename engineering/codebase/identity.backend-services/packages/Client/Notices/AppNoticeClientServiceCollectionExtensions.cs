using Microsoft.Extensions.DependencyInjection;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>Registers the app notice broker over a running Identity instance.</summary>
public static class AppNoticeClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAppNoticeBroker"/> as <see cref="IdentityServiceNoticeBroker"/>, a typed client over the
    /// instance at <paramref name="instance"/>. Chain the be sdk's <c>AddOAuth2ClientCredentials</c> on the result with the
    /// app's client and the instance's notice audience as <c>Resource</c>; the client needs <c>identity.notices.send</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="instance">The instance's address, such as <c>https://id.example.com/</c>; the route resolves under it.</param>
    /// <returns>The client's builder, for its token handler.</returns>
    public static IHttpClientBuilder AddIdentityAppNotices(this IServiceCollection services, Uri instance)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(instance);
        if (!instance.IsAbsoluteUri)
            throw new ArgumentException("The Identity instance's address must be absolute.", nameof(instance));

        var root = instance.AbsoluteUri.EndsWith('/') ? instance : new Uri(instance.AbsoluteUri + "/");
        return services.AddHttpClient<IAppNoticeBroker, IdentityServiceNoticeBroker>(client => client.BaseAddress = root);
    }
}

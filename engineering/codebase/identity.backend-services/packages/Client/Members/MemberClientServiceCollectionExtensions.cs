using Microsoft.Extensions.DependencyInjection;

namespace WoW.Two.Sdk.Beta.Be.Identity.Members;

/// <summary>Registers the member broker over a running Identity instance.</summary>
public static class MemberClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMemberBroker"/> as <see cref="IdentityServiceMemberBroker"/>, a typed client over the instance at
    /// <paramref name="instance"/>. Chain the be sdk's <c>AddOAuth2ClientCredentials</c> on the result with the app's client,
    /// the instance's member audience as <c>Resource</c> and <see cref="MemberConstants.Scope"/> as <c>Scope</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="instance">The instance's address, such as <c>https://id.example.com/</c>; the route resolves under it.</param>
    /// <returns>The client's builder, for its token handler.</returns>
    public static IHttpClientBuilder AddIdentityMembers(this IServiceCollection services, Uri instance)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(instance);
        if (!instance.IsAbsoluteUri)
            throw new ArgumentException("The Identity instance's address must be absolute.", nameof(instance));

        var root = instance.AbsoluteUri.EndsWith('/') ? instance : new Uri(instance.AbsoluteUri + "/");
        return services.AddHttpClient<IMemberBroker, IdentityServiceMemberBroker>(client => client.BaseAddress = root);
    }
}

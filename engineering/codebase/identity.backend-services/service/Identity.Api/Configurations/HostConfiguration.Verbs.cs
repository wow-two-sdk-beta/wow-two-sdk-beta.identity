using WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>
    /// Adds the service's own verbs beside the be sdk's <c>migrate</c> and <c>config check</c>: <c>keys generate</c>, which
    /// prints one PEM bundle before any host boots, and <c>admin create</c>, which makes the instance's first organization
    /// and its owner on the built host without serving.
    /// </summary>
    /// <param name="verbs">The service host's verbs.</param>
    public static ServiceVerbs AddIdentityVerbs(this ServiceVerbs verbs)
    {
        ArgumentNullException.ThrowIfNull(verbs);
        return verbs
            .AddPreHostVerb("keys", verb => SigningKeyCommand.Run(verb.Arguments, verb.Output, verb.Error, TimeProvider.System))
            .AddHostVerb(AdminCommand.Name, (services, verb, ct) => AdminCommand.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct));
    }
}

using Identity.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Meta;

// Answer a verb, or build, configure and serve the host. `keys` prints one PEM bundle to stdout before any host boots;
// `admin create` makes the instance's first admin on the built host without serving; `migrate` and
// `config check` are the be sdk's own.
return await ServiceHost.RunAsync(
    args,
    builder => builder.Configure(),
    app => app.ConfigureAsync(),
    verbs => verbs.AddIdentityVerbs());

public partial class Program;

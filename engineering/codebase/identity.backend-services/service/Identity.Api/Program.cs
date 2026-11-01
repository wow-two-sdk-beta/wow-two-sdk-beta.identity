using Identity.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;
using WoW.Two.Sdk.Beta.Be.Meta;

// Answer a verb, or build, configure and serve the host. `keys` prints one PEM bundle to stdout before any host boots;
// `migrate` and `config check` are the be sdk's own.
return await ServiceHost.RunAsync(
    args,
    builder => builder.Configure(),
    app => app.ConfigureAsync(),
    verbs => verbs.AddPreHostVerb("keys", verb => SigningKeyCommand.Run(verb.Arguments, verb.Output, verb.Error, TimeProvider.System)));

public partial class Program;

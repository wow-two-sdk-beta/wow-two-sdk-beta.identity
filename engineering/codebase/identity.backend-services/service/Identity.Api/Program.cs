using Identity.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

// The owner's key command (keys generate --role …): print one PEM bundle to stdout and exit before any host boots.
if (args is ["keys", .. var keyArguments])
    return SigningKeyCommand.Run(keyArguments, Console.Out, Console.Error, TimeProvider.System);

// Build and configure the host.
var builder = WebApplication.CreateBuilder(args);
builder.Configure();

// Build the app.
var app = builder.Build();

// The deploy's migrate step: apply the identity schema, then exit with the outcome instead of serving.
if (args is ["migrate"])
    return await app.MigrateAsync();

// Configure and run.
await app.ConfigureAsync();
app.Run();
return 0;

public partial class Program;

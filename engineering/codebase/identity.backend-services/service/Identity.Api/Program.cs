using Identity.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

// The owner's key command (keys generate --role …): print one PEM bundle to stdout and exit before any host boots.
if (args is ["keys", .. var keyArguments])
    return SigningKeyCommand.Run(keyArguments, Console.Out, Console.Error, TimeProvider.System);

// Build and configure the host.
var builder = WebApplication.CreateBuilder(args);
builder.Configure();

// Build the app.
var app = builder.Build();

// The be sdk migrate verbs (migrate <verb>; a bare migrate runs apply, then seed): exit with the verb's code instead of
// serving, before any startup work runs.
if (MigrateCommand.IsCommand(args))
    return await app.RunMigrateCommandAsync(args);

// Configure and run.
await app.ConfigureAsync();
app.Run();
return 0;

public partial class Program;

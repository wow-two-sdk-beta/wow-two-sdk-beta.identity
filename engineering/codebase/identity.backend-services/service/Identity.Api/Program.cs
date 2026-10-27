using Identity.Api.Configurations;

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

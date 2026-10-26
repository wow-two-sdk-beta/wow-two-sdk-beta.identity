using Identity.Api.Configurations;

// Build and configure the host.
var builder = WebApplication.CreateBuilder(args);
builder.Configure();

// Build and configure the app.
var app = builder.Build();
await app.ConfigureAsync();

// Run.
app.Run();

public partial class Program;

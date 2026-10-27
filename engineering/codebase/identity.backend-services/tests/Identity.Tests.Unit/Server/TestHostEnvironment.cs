using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>A host environment for key tests: <c>Development</c> unless set otherwise.</summary>
internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "Identity.Tests";

    public string ContentRootPath { get; set; } = Path.GetTempPath();

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

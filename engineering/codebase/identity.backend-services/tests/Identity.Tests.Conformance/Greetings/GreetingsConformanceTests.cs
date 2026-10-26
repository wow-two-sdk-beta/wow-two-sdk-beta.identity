using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;
using WoW.Two.Sdk.Beta.Be.Identity.Testing.Greetings;

namespace Identity.Tests.Conformance.Greetings;

/// <summary>Form 1: the contract's suite against the in-process module.</summary>
public sealed class ModuleGreetingsConformanceTests(ModuleFixture fixture)
    : GreetingsBrokerConformance, IClassFixture<ModuleFixture>, IDisposable
{
    private readonly AsyncServiceScope scope = fixture.Services.CreateAsyncScope();

    protected override IGreetingsBroker Broker => scope.ServiceProvider.GetRequiredService<IGreetingsBroker>();

    protected override TimeProvider Clock => fixture.Clock;

    public void Dispose() => scope.Dispose();
}

/// <summary>Forms 2 and 3: the contract's suite through the HTTP client against the service host.</summary>
public sealed class ClientGreetingsConformanceTests(ServiceFixture fixture)
    : GreetingsBrokerConformance, IClassFixture<ServiceFixture>
{
    protected override IGreetingsBroker Broker => fixture.Services.GetRequiredService<IGreetingsBroker>();

    protected override TimeProvider Clock => fixture.Clock;
}

/// <summary>The test kit's fake keeps the contract too, so a consumer's tests rely on it as on the real thing.</summary>
public sealed class FakeGreetingsConformanceTests : GreetingsBrokerConformance
{
    private readonly TimeProvider clock = TimeProvider.System;

    protected override IGreetingsBroker Broker => new FakeGreetingsBroker(clock);

    protected override TimeProvider Clock => clock;
}

using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;
using WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;
using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Signals;

/// <summary>Long operations hearing signals: a role change and a sign-out let background work continue, access loss cancels it.</summary>
public sealed class PersonOperationBridgeTests
{
    private const string Issuer = "https://id.example.test/";
    private const string Target = "https://jobs.example.test/signals/identity";
    private const string Route = "/signals/identity";

    private readonly RsaSecurityKey _key = SignedTokens.NewKey();
    private readonly SignalCapture _capture = new();
    private IServiceProvider _provider = null!;

    private SignalRequests Signals => new(_key, Issuer, Target, Route);

    private IPersonOperationTracker Tracker => _provider.GetRequiredService<IPersonOperationTracker>();

    [Fact]
    public async Task SignOut_ShouldLetBackgroundWorkContinue_AndCancelWorkTiedToTheSession()
    {
        await using var host = await StartAsync(tracked: true);
        using var background = Tracker.Begin(new PersonOperationDeclaration { Name = "export", Subject = "person-1" });
        using var live = Tracker.Begin(new PersonOperationDeclaration { Name = "stream", Subject = "person-1", SessionId = "sid-1" });

        await SendAsync(host, Signals.SessionEnded("sid-1", "person-1"));

        background.CancellationToken.IsCancellationRequested.Should().BeFalse();
        live.CancellationToken.IsCancellationRequested.Should().BeTrue();
        live.CancelReason.Should().Be(PersonOperationCancelReasonConstants.SessionEnded);
    }

    [Fact]
    public async Task Removed_ShouldCancelThePersonsWorkInThatOrganization_AndNoOthers()
    {
        await using var host = await StartAsync(tracked: true);
        using var inOrganization = Tracker.Begin(new PersonOperationDeclaration { Name = "export", Subject = "person-1", OrganizationId = "org-1" });
        using var elsewhere = Tracker.Begin(new PersonOperationDeclaration { Name = "export", Subject = "person-1", OrganizationId = "org-2" });
        using var someoneElse = Tracker.Begin(new PersonOperationDeclaration { Name = "export", Subject = "person-2", OrganizationId = "org-1" });

        await SendAsync(host, Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Removed));

        inOrganization.CancellationToken.IsCancellationRequested.Should().BeTrue();
        inOrganization.CancelReason.Should().Be(PersonOperationCancelReasonConstants.AccessLost);
        elsewhere.CancellationToken.IsCancellationRequested.Should().BeFalse();
        someoneElse.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Changed_ShouldLetTheWorkContinue()
    {
        await using var host = await StartAsync(tracked: true);
        using var work = Tracker.Begin(new PersonOperationDeclaration { Name = "export", Subject = "person-1", OrganizationId = "org-1" });

        await SendAsync(host, Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Changed));

        work.CancellationToken.IsCancellationRequested.Should().BeFalse("a role change continues");
    }

    [Fact]
    public async Task NoTracker_ShouldStillBootAndHandle()
    {
        await using var host = await StartAsync(tracked: false);

        await SendAsync(host, Signals.SessionEnded("sid-1", "person-1"));
        await SendAsync(host, Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Removed));

        _provider.GetService<IPersonOperationTracker>().Should().BeNull();
        _capture.Sessions.Should().ContainSingle();
        _capture.Accesses.Should().ContainSingle();
    }

    private async Task SendAsync<TEvent>(ResourceHost host, TEvent signal)
        where TEvent : class
    {
        var before = _capture.Sessions.Count + _capture.Accesses.Count;
        using var response = await host.Client.SendAsync(Signals.Post(signal));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(() => _capture.Sessions.Count + _capture.Accesses.Count > before, "the handlers ran, in registration order, the capture last");
    }

    private Task<ResourceHost> StartAsync(bool tracked)
        => ResourceHost.StartAsync(
            services =>
            {
                services.AddAuthentication();
                if (tracked)
                    services.AddPersonOperations();

                services.AddIdentitySignalReceiver(options =>
                {
                    options.Audience = Target;
                    options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer, SigningKeys = [SignedTokens.PublicOf(_key)] });
                });
                services.AddSingleton<IEventHandler<SessionEndedEvent>>(_capture);
                services.AddSingleton<IEventHandler<AccessChangedEvent>>(_capture);
            },
            endpoints =>
            {
                _provider = endpoints.ServiceProvider;
                endpoints.MapIdentitySignals(Route);
            });
}

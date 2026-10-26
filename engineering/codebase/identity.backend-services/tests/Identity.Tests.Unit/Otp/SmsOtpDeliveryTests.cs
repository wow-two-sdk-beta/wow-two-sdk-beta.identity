using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Comms.Sms;
using WoW.Two.Sdk.Beta.Be.Comms.Sms.Eskiz;
using WoW.Two.Sdk.Beta.Be.Comms.Sms.Twilio;
using WoW.Two.Sdk.Beta.Be.Comms.Sms.Vonage;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Models;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Sms;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Otp;

/// <summary>OTP delivery over the core SMS brokers, against scripted provider responses: template formatting and the
/// named broker a host configures. Moved from the be sdk's <c>Foundation.Tests/Comms/SmsBrokerTests.cs</c> with Identity.</summary>
public sealed class SmsOtpDeliveryTests
{
    [Fact]
    public async Task SmsOtpDelivery_ShouldFormatTheTemplateAndForwardTheBrokerOutcome()
    {
        var provider = new ScriptedProvider(_ => Json(HttpStatusCode.OK, """{"messages":[{"status":"0","message-id":"V-9"}]}"""));
        var services = new ServiceCollection();
        services.AddOtpService();
        services.AddSmsOtpDelivery(o => o.ScopeDisplayNames["login"] = "Acme");
        services.AddVonageSmsBroker(o => { o.ApiKey = "k"; o.ApiSecret = "s"; }).ConfigurePrimaryHttpMessageHandler(() => provider);
        services.AddSmsDefaults(o => o.DefaultFrom = "Acme");
        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IOtpDeliveryHandler>().OfType<SmsOtpDeliveryHandler>().Single();

        var result = await handler.SendAsync(new OtpDeliveryEnvelopeModel { DeliveryAddress = "+998901234567", Code = "123456", Scope = "login" });

        result.Success.Should().BeTrue();
        provider.Requests.Single().Body.Should().Contain(Uri.EscapeDataString("123456 is your Acme code. It expires in 5 minutes.").Replace("%20", "+", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostConfiguration_ShouldDecideTheBrokers_AndOtpDeliveryShouldFollowTheNamedOne()
    {
        var provider = new ScriptedProvider(request => request.Path switch
        {
            "/api/auth/login" => Json(HttpStatusCode.OK, """{"data":{"token":"t1"}}"""),
            "/api/message/sms/send" => Json(HttpStatusCode.OK, """{"id":"E-1","status":"waiting"}"""),
            _ => Json(HttpStatusCode.Created, """{"sid":"SM1"}"""),
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Comms:Sms:DefaultFrom"] = "Acme",
            ["Comms:Sms:DefaultBroker"] = "eskiz",
            ["Comms:Sms:Twilio:AccountSid"] = "AC1",
            ["Comms:Sms:Twilio:AuthToken"] = "secret",
            ["Comms:Sms:Eskiz:Email"] = "a@b.uz",
            ["Comms:Sms:Eskiz:Password"] = "p",
            ["Identity:Otp:Sms:Broker"] = "twilio",
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddSmsBrokers(configuration);
        services.AddSmsOtpDelivery();
        foreach (var name in new[] { TwilioSmsBroker.HttpClientName, EskizSmsBroker.HttpClientName })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => provider);
        await using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISmsBrokerFactory>();

        factory.Create().Should().BeOfType<EskizSmsBroker>();
        factory.Create("twilio").Should().BeOfType<TwilioSmsBroker>();
        factory.Create("vonage").Should().BeNull();

        await using var scope = serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IOtpDeliveryHandler>(OtpChannelNameConstants.Sms);
        var envelope = new OtpDeliveryEnvelopeModel { DeliveryAddress = "+998901234567", Code = "123456", Scope = "login", Text = "Code 123456" };
        (await handler.SendAsync(envelope)).Success.Should().BeTrue();
        (await handler.SendAsync(envelope with { Broker = "eskiz" })).Success.Should().BeTrue();
        (await handler.SendAsync(envelope with { Broker = "vonage" })).FailureReason.Should().Be("sms_broker_not_registered: vonage");

        provider.Requests.Should().HaveCount(3);
        provider.Requests[0].Path.Should().Be("/2010-04-01/Accounts/AC1/Messages.json");
        provider.Requests[0].Body.Should().Contain("Body=Code+123456").And.Contain("From=Acme");
        provider.Requests[2].Path.Should().Be("/api/message/sms/send");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest
    {
        public required string Path { get; init; }

        public string? Authorization { get; init; }

        public required string Body { get; init; }
    }

    /// <summary>Answers each request from a script and records what the broker sent.</summary>
    private sealed class ScriptedProvider(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Func<RecordedRequest, HttpResponseMessage> Respond { get; set; } = respond;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest
            {
                Path = request.RequestUri!.AbsolutePath,
                Authorization = request.Headers.Authorization?.ToString(),
                Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
            };
            Requests.Add(recorded);
            return Respond(recorded);
        }
    }
}

using WoW.Two.Sdk.Beta.Be.Identity.Greetings;

namespace WoW.Two.Sdk.Beta.Be.Identity.Client.Greetings;

/// <summary>Represents the service's success envelope around a greeting.</summary>
/// <param name="Data">The greeting.</param>
internal sealed record GreetingEnvelope(GreetingModel? Data);

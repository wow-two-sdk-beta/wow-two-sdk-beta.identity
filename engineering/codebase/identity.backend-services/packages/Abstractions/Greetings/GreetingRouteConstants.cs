namespace WoW.Two.Sdk.Beta.Be.Identity.Greetings;

/// <summary>Holds the HTTP routes of the greetings contract, shared by the service and the client.</summary>
public static class GreetingRouteConstants
{
    /// <summary>Holds the greetings resource, relative to the service root.</summary>
    public const string Greetings = "api/greetings";

    /// <summary>Holds the query parameter that carries the name.</summary>
    public const string NameParameter = "name";
}

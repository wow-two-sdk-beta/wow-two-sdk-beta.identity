using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Identity.Greetings;

/// <summary>Holds the error catalogue of the greetings contract.</summary>
public static class GreetingErrors
{
    /// <summary>Creates the error for a name longer than <see cref="GreetingConstants.MaxNameLength"/>.</summary>
    public static AppError NameTooLong() =>
        AppErrorFactory.Validation($"The name must be at most {GreetingConstants.MaxNameLength} characters.");
}

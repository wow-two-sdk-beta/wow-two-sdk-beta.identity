namespace Identity.Api.Console;

/// <summary>Holds the console's own refusal codes, read from the problem's <c>identityCode</c> beside the catalog's.</summary>
public static class ConsoleErrorCodeConstants
{
    /// <summary>Holds the code of a preset organization id another organization already holds: <c>409</c>.</summary>
    public const string DuplicateOrganizationId = "DuplicateOrganizationId";
}

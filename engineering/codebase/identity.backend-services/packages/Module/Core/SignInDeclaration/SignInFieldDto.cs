namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>One field of a method's form: its name in the request, its input kind and the rules the browser checks first.</summary>
public sealed record SignInFieldDto
{
    /// <summary>The request property the field fills, such as <c>login</c> or <c>phone</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The input the field takes (<see cref="SignInFieldKindConstants"/>), such as <c>phone</c> or <c>pin</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>A phone field's allowed countries, ISO 3166-1 alpha-2 codes; empty allows every country; null for other kinds.</summary>
    public IReadOnlyList<string>? Countries { get; init; }

    /// <summary>The rules in the order a form checks them; the server checks each again.</summary>
    public required IReadOnlyList<SignInRuleDto> Rules { get; init; }
}

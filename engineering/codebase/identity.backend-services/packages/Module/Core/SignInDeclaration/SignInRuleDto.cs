namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>One rule a field meets: a fe sdk <c>ValidationCodes</c> code, its operands and its message in the request's locale.</summary>
public sealed record SignInRuleDto
{
    /// <summary>The rule code (<see cref="SignInRuleCodeConstants"/>), such as <c>required</c>, <c>min</c> or <c>pattern</c>.</summary>
    public required string Code { get; init; }

    /// <summary>The rule's operands under the vocabulary's names, such as <c>{ "min": 15, "unit": "characters" }</c>; null when it has none.</summary>
    public IReadOnlyDictionary<string, object>? Params { get; init; }

    /// <summary>The text a person reads when the value breaks the rule; the server's answer for the same rule says the same.</summary>
    public required string Message { get; init; }
}

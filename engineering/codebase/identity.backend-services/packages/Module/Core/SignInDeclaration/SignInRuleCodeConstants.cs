namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>
/// Holds the rule codes the declaration uses: the fe sdk's <c>ValidationCodes</c> names, so the browser's check and the
/// server's answer speak one vocabulary. Their operands take the vocabulary's names too (<c>min</c>, <c>max</c>,
/// <c>length</c>, <c>unit</c>).
/// </summary>
public static class SignInRuleCodeConstants
{
    /// <summary>A value must be present.</summary>
    public const string Required = "required";

    /// <summary>At least <c>min</c> of <c>unit</c>.</summary>
    public const string Min = "min";

    /// <summary>At most <c>max</c> of <c>unit</c>.</summary>
    public const string Max = "max";

    /// <summary>Exactly <c>length</c> of <c>unit</c>.</summary>
    public const string Length = "length";

    /// <summary>The value holds a match of <c>pattern</c>, a JavaScript regular expression with the <c>u</c> flag.</summary>
    public const string Pattern = "pattern";

    /// <summary>The value differs from each field of <c>fields</c>, ignoring case when <c>isCaseIgnored</c>.</summary>
    public const string NotEqual = "notEqual";

    /// <summary>A rule the vocabulary has no code for, named by <c>rule</c>, such as <c>distinctCharacters</c>.</summary>
    public const string Custom = "custom";

    /// <summary>The <c>unit</c> operand of a size rule counted in characters.</summary>
    public const string CharactersUnit = "characters";

    /// <summary>The <c>rule</c> operand of a <see cref="Custom"/> rule needing at least <c>min</c> different characters.</summary>
    public const string DistinctCharactersRule = "distinctCharacters";
}

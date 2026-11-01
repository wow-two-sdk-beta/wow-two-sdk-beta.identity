namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Asks for a sign-in code for a phone number; answered the same whether an account holds it or not.</summary>
public sealed record PhoneSignInSendApiRequest
{
    /// <summary>The number in E.164, such as <c>+998901234567</c>.</summary>
    public required string Phone { get; init; }
}

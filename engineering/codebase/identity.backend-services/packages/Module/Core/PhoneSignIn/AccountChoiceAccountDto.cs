namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>One account a trusted contact's verified number may sign in as.</summary>
public sealed record AccountChoiceAccountDto
{
    /// <summary>The account; <c>phone-sign-in/choose</c> takes it as <c>userId</c>.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The name the account goes by, or its masked phone when it set none.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The account's picture link; null without one.</summary>
    public string? PictureUrl { get; init; }

    /// <summary>Whether this is the contact's own account.</summary>
    public required bool IsSelf { get; init; }
}

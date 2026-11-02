namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>The profile fields an invitation sign-up asks for (<c>Identity:SignUp:Profile</c>), with the picture's rules.</summary>
public sealed record SignUpProfileDto
{
    /// <summary>Whether the sign-up asks for a name: <c>off</c>, <c>optional</c> or <c>required</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Whether the sign-up asks for a picture: <c>off</c>, <c>optional</c> or <c>required</c>.</summary>
    public required string Picture { get; init; }

    /// <summary>The rules an uploaded picture meets (<c>Identity:ProfilePictures</c>).</summary>
    public required PictureRulesDto PictureRules { get; init; }
}

/// <summary>The rules an uploaded profile picture meets; the server checks each again, the type by the file's leading bytes.</summary>
public sealed record PictureRulesDto
{
    /// <summary>The media types an upload may be; a file input's <c>accept</c> lists exactly these.</summary>
    public required IReadOnlyList<string> AcceptedTypes { get; init; }

    /// <summary>The largest upload, in bytes.</summary>
    public required int MaxBytes { get; init; }

    /// <summary>The shortest side an upload may have, in pixels.</summary>
    public required int MinDimension { get; init; }

    /// <summary>The longest side an upload may have, in pixels.</summary>
    public required int MaxDimension { get; init; }
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>
/// Holds which sign-in methods and channels the account API serves beside the phone code, which follows the bot
/// (<c>Identity:Telegram:IsEnabled</c>). A phone-only instance turns both off: no password, no forgot-password and no email
/// on the API or the hosted pages.
/// </summary>
/// <remarks>
/// Set in code with <c>.AddAccountEndpoints(methods: o => …)</c> or in the host section <c>Identity:Methods</c>, which is
/// applied last and read live. A route whose method is off answers <c>404</c> (<c>SignInMethodOff</c>), and the sign-in
/// declaration leaves it out:
///   - <c>Password</c> serves <c>sign-in</c>, <c>sign-up</c>, <c>forgot-password</c>, <c>reset-password</c> and the
///     password change routes
///   - <c>Email</c> serves <c>sign-up</c>, <c>confirm-email</c>, <c>resend-confirmation-email</c>, <c>forgot-password</c>,
///     <c>reset-password</c> and email sign-in
/// </remarks>
public sealed record SignInMethodOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Methods";

    /// <summary>Names the password method in <see cref="IsOn"/>.</summary>
    public const string PasswordMethod = "password";

    /// <summary>Names the email channel in <see cref="IsOn"/>.</summary>
    public const string EmailMethod = "email";

    /// <summary>Gets the password switch (<c>Identity:Methods:Password</c>). On by default.</summary>
    public SignInMethodSwitch Password { get; } = new();

    /// <summary>Gets the email switch (<c>Identity:Methods:Email</c>). On by default.</summary>
    public SignInMethodSwitch Email { get; } = new();

    /// <summary>Whether <paramref name="method"/> is on: <see cref="PasswordMethod"/> or <see cref="EmailMethod"/>.</summary>
    /// <param name="method">The method's name.</param>
    public bool IsOn(string method) => method switch
    {
        PasswordMethod => Password.IsEnabled,
        EmailMethod => Email.IsEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "The method is neither password nor email."),
    };
}

/// <summary>Holds one method's switch under <c>Identity:Methods</c>.</summary>
public sealed record SignInMethodSwitch
{
    /// <summary>Gets or sets whether the method is on. Default true.</summary>
    public bool IsEnabled { get; set; } = true;
}

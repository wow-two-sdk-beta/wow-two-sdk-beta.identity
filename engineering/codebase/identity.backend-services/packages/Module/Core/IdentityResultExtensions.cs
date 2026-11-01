using WoW.Two.Sdk.Beta.Be.Foundation.Validation;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Converts identity failures into the SDK's validation error, one field error per identity error.</summary>
public static class IdentityResultExtensions
{
    /// <summary>
    /// The failures of <paramref name="result"/> as a <see cref="ValidationError"/>: each field error keeps the identity
    /// code and its values, so error translation catalogs match it, and names the request field it concerns.
    /// </summary>
    /// <param name="result">A failed identity result.</param>
    /// <exception cref="InvalidOperationException">The result succeeded.</exception>
    public static ValidationError ToValidationError(this IdentityResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("A successful identity result carries no validation error.");

        return ValidationError.From([.. result.Errors.Select(error => new FieldError
        {
            Property = FieldOf(error.Code),
            Code = error.Code,
            Message = error.Description,
            Params = error.Params,
        })]);
    }

    /// <summary>The request field an identity code concerns; empty for account-wide failures.</summary>
    /// <param name="code">The identity error code.</param>
    public static string FieldOf(string code) => code switch
    {
        IdentityErrorCodeConstants.PasswordMismatch => "currentPassword",
        _ when code.StartsWith("Password", StringComparison.Ordinal) => "password",
        IdentityErrorCodeConstants.UserAlreadyHasPassword => "password",
        IdentityErrorCodeConstants.DuplicateEmail => "email",
        IdentityErrorCodeConstants.UserNameRequired or IdentityErrorCodeConstants.DuplicateUserName => "userName",
        IdentityErrorCodeConstants.InvalidToken or PasswordChangeErrorCodeConstants.InvalidLink => "token",
        PasswordChangeErrorCodeConstants.InvalidCode => "code",
        IdentityErrorCodeConstants.InvalidAuthenticatorCode
            or IdentityErrorCodeConstants.InvalidRecoveryCode
            or IdentityErrorCodeConstants.InvalidPhoneCode => "code",
        IdentityErrorCodeConstants.RoleNameRequired
            or IdentityErrorCodeConstants.DuplicateRoleName
            or IdentityErrorCodeConstants.RoleNotFound
            or IdentityErrorCodeConstants.UserAlreadyInRole
            or IdentityErrorCodeConstants.UserNotInRole => "role",
        _ => string.Empty,
    };
}

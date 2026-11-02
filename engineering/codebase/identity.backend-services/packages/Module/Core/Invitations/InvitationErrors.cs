using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>Turns refusals of invitations and memberships into coded API errors.</summary>
internal static class InvitationErrors
{
    /// <summary>
    /// The API error for <paramref name="refusal"/>, its code in the problem's <c>identityCode</c> extension and message key:
    /// <c>404</c> for an unknown link or member, <c>409</c> for a used, expired or withdrawn invitation and the last owner,
    /// <c>403</c> for an invitation naming another identifier, else <c>400</c>.
    /// </summary>
    /// <param name="refusal">The refused result.</param>
    public static AppError Of(IdentityResult refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var hasError = refusal.Errors.Count > 0;
        var code = hasError ? refusal.Errors[0].Code : IdentityErrorCodeConstants.InvitationNotFound;
        var type = code switch
        {
            IdentityErrorCodeConstants.InvitationNotFound or IdentityErrorCodeConstants.UserNotMember or IdentityErrorCodeConstants.OrganizationNotFound => AppErrorType.NotFound,
            IdentityErrorCodeConstants.InvitationExpired or IdentityErrorCodeConstants.InvitationAccepted or IdentityErrorCodeConstants.InvitationRevoked
                or IdentityErrorCodeConstants.LastOrganizationOwner or IdentityErrorCodeConstants.UserAlreadyMember => AppErrorType.Conflict,
            IdentityErrorCodeConstants.InvitationIdentifierMismatch or IdentityErrorCodeConstants.OrganizationManagerRequired
                or IdentityErrorCodeConstants.OrganizationOwnerRequired => AppErrorType.Forbidden,
            _ => AppErrorType.Validation,
        };
        return Coded(type, hasError ? refusal.Errors[0].Description : "The invitation link is not valid.", code);
    }

    /// <summary>An error whose code a client reads from the problem's <c>identityCode</c> extension, beside the message key translation uses.</summary>
    /// <param name="type">The error type.</param>
    /// <param name="message">The message.</param>
    /// <param name="code">The identity code.</param>
    public static AppError Coded(AppErrorType type, string message, string code)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal) { ["messageKey"] = code, ["identityCode"] = code };
        metadata[AppErrorProblemDetailsFactory.ExtensionsMetadataKey] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["identityCode"] = code };
        return AppError.Of(type, message, metadata);
    }
}

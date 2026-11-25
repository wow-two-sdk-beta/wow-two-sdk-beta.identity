using Identity.Application.Console.Constants;
using Identity.Application.Console.Commands;
using Identity.Application.Console.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace Identity.Infrastructure.Console.CommandHandlers;

/// <inheritdoc />
public sealed class CreateConsoleOrganizationCommandHandler(UserAccountService accounts, OrganizationService organizations)
    : ICommandHandler<CreateConsoleOrganizationCommand, AppResult<ConsoleOrganization>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ConsoleOrganization>> HandleAsync(CreateConsoleOrganizationCommand command, CancellationToken cancellationToken)
    {
        if (command.CallerId is not { } userId || await accounts.FindByIdAsync(userId, cancellationToken) is not { } user)
            return AppResult<ConsoleOrganization>.Fail(AppErrorFactory.Unauthorized());

        var id = command.OrganizationId ?? Guid.Empty;
        if (id != Guid.Empty && await organizations.FindByIdAsync(id, cancellationToken) is { } holder)
        {
            var conflict = Coded(AppErrorType.Conflict, $"Organization {id} already exists, with the handle '{holder.Slug}'.", ConsoleErrorCodeConstants.DuplicateOrganizationId);
            return AppResult<ConsoleOrganization>.Fail(conflict);
        }

        var organization = new IdentityOrganization { Id = id, Name = command.Name ?? string.Empty, Slug = command.Slug ?? string.Empty };
        var created = await organizations.CreateAsync(organization, user, cancellationToken);
        if (!created.IsSuccess)
        {
            var refusal = created.Errors[0];
            var type = refusal.Code == IdentityErrorCodeConstants.DuplicateOrganizationSlug ? AppErrorType.Conflict : AppErrorType.Validation;
            return AppResult<ConsoleOrganization>.Fail(Coded(type, refusal.Description, refusal.Code));
        }

        var role = await organizations.GetRoleAsync(organization, user, cancellationToken) ?? OrganizationRoleConstants.Owner;
        return AppResult<ConsoleOrganization>.Ok(new ConsoleOrganization(organization.Id, organization.Name, organization.Slug, role));
    }

    private static AppError Coded(AppErrorType type, string message, string code)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal) { ["messageKey"] = code, ["identityCode"] = code };
        metadata[AppErrorProblemDetailsFactory.ExtensionsMetadataKey] = new Dictionary<string, object?> { ["identityCode"] = code };
        return AppError.Of(type, message, metadata);
    }
}

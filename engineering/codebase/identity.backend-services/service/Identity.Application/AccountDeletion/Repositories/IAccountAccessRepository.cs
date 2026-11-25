namespace Identity.Application.AccountDeletion.Repositories;

/// <summary>Reads role assignments that prevent product-managed deletion.</summary>
public interface IAccountAccessRepository
{
    Task<bool> HasOrganizationRoleAsync(Guid personId, CancellationToken cancellationToken);
    Task<bool> HasInstanceRoleAsync(Guid personId, CancellationToken cancellationToken);
}

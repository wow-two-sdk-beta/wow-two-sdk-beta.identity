using Identity.Application.Status.Models;
using System.Reflection;
using Identity.Application.Status.Queries;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Infrastructure.Status.QueryHandlers;

/// <inheritdoc />
public sealed class GetSystemStatusQueryHandler(IHostEnvironment host) : IQueryHandler<GetSystemStatusQuery, AppResult<IdentitySystemStatus>>
{
    /// <inheritdoc />
    public ValueTask<AppResult<IdentitySystemStatus>> HandleAsync(GetSystemStatusQuery query, CancellationToken cancellationToken)
    {
        var version = Assembly.Load(host.ApplicationName)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return ValueTask.FromResult(AppResult<IdentitySystemStatus>.Ok(new IdentitySystemStatus(version)));
    }
}

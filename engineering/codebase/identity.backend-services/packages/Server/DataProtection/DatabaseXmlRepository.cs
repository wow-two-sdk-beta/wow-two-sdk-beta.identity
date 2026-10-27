using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.DataProtection;

/// <summary>
/// Keeps the Data Protection ring in <c>identity_data_protection_keys</c> through <see cref="IdentityDbContext"/>, so every
/// replica of an instance shares one ring and a container replacement keeps every cookie.
/// </summary>
/// <param name="services">The root provider; each call opens its own scope, as the key manager runs outside requests.</param>
/// <param name="logger">Receives the store event.</param>
internal sealed partial class DatabaseXmlRepository(IServiceProvider services, ILogger<DatabaseXmlRepository> logger) : IXmlRepository
{
    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IdentityDbContext>().DataProtectionKeys
            .AsNoTracking()
            .OrderBy(key => key.Id)
            .Select(key => key.Xml)
            .AsEnumerable()
            .Select(xml => XElement.Parse(xml))
            .ToList();
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        context.DataProtectionKeys.Add(new IdentityDataProtectionKey { FriendlyName = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });
        context.SaveChanges();
        LogStored(logger, friendlyName);
    }

    [LoggerMessage(EventId = 7201, Level = LogLevel.Information, Message = "Data Protection key {FriendlyName} stored in the identity database")]
    private static partial void LogStored(ILogger logger, string friendlyName);
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

/// <summary>Opts personal-data export and account deletion into an identity registration.</summary>
public static class PersonalDataIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="UserPersonalDataService"/> over <see cref="IdentityDbContext"/>. Products add
    /// <see cref="IPersonalDataExporter"/> sections and <see cref="IAccountDeletionHandler"/> steps with
    /// <c>TryAddEnumerable</c>. The account API then maps <c>manage/personal-data</c> and <c>manage/delete-account</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddPersonalData(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<UserPersonalDataService>();
        return builder;
    }
}

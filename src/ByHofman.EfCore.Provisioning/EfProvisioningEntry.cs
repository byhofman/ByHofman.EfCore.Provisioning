using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;

namespace ByHofman.EfCore.Provisioning;

public sealed class EfProvisioningEntry
{
    internal Func<IApplicationBuilder, CancellationToken, Task> ExecuteAsync { get; }

    private EfProvisioningEntry(Func<IApplicationBuilder, CancellationToken, Task> execute)
        => ExecuteAsync = execute;

    public static EfProvisioningEntry For<TDbContext>(
        Action<EfProvisioningOptions<TDbContext>>? configureOptions = null,
        string? configSectionKey = null)
        where TDbContext : DbContext
        => new((app, ct) => app.ProvisionAsync<TDbContext>(configureOptions, configSectionKey, ct));
}

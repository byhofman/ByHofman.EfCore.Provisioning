using Microsoft.EntityFrameworkCore;

namespace ByHofman.EfCore.Provisioning;

public class EfProvisioningOptions<TDbContext> where TDbContext : DbContext
{
    public const string SectionName = "EfProvisioning";

    public bool CheckMigrations { get; set; } = true;
    public bool ApplyMigrations { get; set; } = true;

    /// <summary>
    /// Restricts migration application to specific environment names (e.g. ["Development", "Staging"]).
    /// Null or empty means migrations are applied in all environments.
    /// </summary>
    public string[]? MigrateOnEnvironments { get; set; }

    /// <summary>
    /// Maximum number of retry attempts if provisioning fails (e.g. DB not yet reachable).
    /// 0 means no retries — fail immediately.
    /// </summary>
    public int MaxRetries { get; set; } = 0;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public Func<TDbContext, IServiceProvider, CancellationToken, Task>? Seeder { get; set; }
}

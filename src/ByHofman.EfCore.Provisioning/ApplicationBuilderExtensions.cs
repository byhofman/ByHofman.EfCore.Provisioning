using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ByHofman.EfCore.Provisioning;

public static class ApplicationBuilderExtensions
{
    public static async Task<IApplicationBuilder> ProvisionAsync<TDbContext>(
        this IApplicationBuilder app,
        Action<EfProvisioningOptions<TDbContext>>? configureOptions = null,
        string? configSectionKey = null,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var sectionKey = configSectionKey ?? EfProvisioningOptions<TDbContext>.SectionName;
        var options = new EfProvisioningOptions<TDbContext>();

        // Config binding first — inline lambda wins over config values
        app.ApplicationServices.GetService<IConfiguration>()
            ?.GetSection(sectionKey)
            .Bind(options);

        configureOptions?.Invoke(options);

        var rootLogger = app.ApplicationServices.GetService<ILogger<TDbContext>>();
        var env = app.ApplicationServices.GetService<IHostEnvironment>();

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var scope = app.ApplicationServices.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
                var logger = scope.ServiceProvider.GetService<ILogger<TDbContext>>();

                if (options.CheckMigrations)
                {
                    var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                    var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

                    if (pending.Count == 0)
                    {
                        logger?.LogInformation(
                            "No pending migrations for {DbContext} ({AppliedCount} applied)",
                            typeof(TDbContext).Name, applied.Count);
                    }
                    else
                    {
                        logger?.LogInformation(
                            "Found {PendingCount} pending migration(s) for {DbContext} ({AppliedCount} applied): {PendingMigrations}",
                            pending.Count, typeof(TDbContext).Name, applied.Count, pending);
                    }
                }

                if (options.ApplyMigrations)
                {
                    var skipDueToEnvironment = options.MigrateOnEnvironments is { Length: > 0 }
                        && (env?.EnvironmentName is not { } envName
                            || !options.MigrateOnEnvironments.Contains(envName, StringComparer.OrdinalIgnoreCase));

                    if (skipDueToEnvironment)
                    {
                        logger?.LogInformation(
                            "Skipping migrations for {DbContext} — environment '{Environment}' not in MigrateOnEnvironments",
                            typeof(TDbContext).Name, env?.EnvironmentName ?? "unknown");
                    }
                    else
                    {
                        logger?.LogInformation("Applying migrations for {DbContext}...", typeof(TDbContext).Name);
                        await context.Database.MigrateAsync(cancellationToken);
                        logger?.LogInformation("Migrations applied successfully for {DbContext}", typeof(TDbContext).Name);
                    }
                }

                if (options.Seeder is not null)
                {
                    logger?.LogInformation("Running seeder for {DbContext}...", typeof(TDbContext).Name);
                    await options.Seeder(context, scope.ServiceProvider, cancellationToken);
                    logger?.LogInformation("Seeder completed for {DbContext}", typeof(TDbContext).Name);
                }

                break;
            }
            catch (Exception ex)
            {
                if (attempt >= options.MaxRetries)
                {
                    rootLogger?.LogError(ex,
                        "Database provisioning for {DbContext} failed after {Attempts} attempt(s)",
                        typeof(TDbContext).Name, attempt + 1);
                    throw;
                }

                rootLogger?.LogWarning(ex,
                    "Database provisioning for {DbContext} attempt {Attempt}/{MaxAttempts} failed, retrying in {Delay}",
                    typeof(TDbContext).Name, attempt + 1, options.MaxRetries + 1, options.RetryDelay);

                await Task.Delay(options.RetryDelay, cancellationToken);
            }
        }

        return app;
    }

    public static async Task<IApplicationBuilder> ProvisionAllAsync(
        this IApplicationBuilder app,
        CancellationToken cancellationToken,
        params EfProvisioningEntry[] entries)
    {
        foreach (var entry in entries)
            await entry.ExecuteAsync(app, cancellationToken);
        return app;
    }

    public static Task<IApplicationBuilder> ProvisionAllAsync(
        this IApplicationBuilder app,
        params EfProvisioningEntry[] entries)
        => app.ProvisionAllAsync(CancellationToken.None, entries);
}

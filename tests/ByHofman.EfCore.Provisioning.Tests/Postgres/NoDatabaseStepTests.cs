using ByHofman.EfCore.Provisioning.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ByHofman.EfCore.Provisioning.Tests.Postgres;

/// <summary>
/// An app that starts with provisioning switched off must start with no database reachable (Omway's API
/// tests boot that way). Nothing listens on port 1, so any connection attempt fails the test.
/// </summary>
public sealed class NoDatabaseStepTests
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=contoso;Username=contoso;Password=contoso;Timeout=2";

    [Fact]
    public async Task WithEveryStepOff_NoConnectionIsOpened()
    {
        var act = () => App().ProvisionAsync<RaceDbContext>(options =>
        {
            options.CheckMigrations = false;
            options.ApplyMigrations = false;
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WithMigrationsHeldOffByEnvironment_NoConnectionIsOpened()
    {
        var act = () => App(Environments.Development).ProvisionAsync<RaceDbContext>(options =>
        {
            options.CheckMigrations = false;
            options.ApplyMigrations = true;
            options.MigrateOnEnvironments = [Environments.Staging];
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AStepThatRuns_StillNeedsTheDatabase()
    {
        var act = () => App().ProvisionAsync<RaceDbContext>(options =>
        {
            options.CheckMigrations = true;
            options.ApplyMigrations = false;
        });

        await act.Should().ThrowAsync<Exception>();
    }

    private static FakeApplicationBuilder App(string? environment = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(RaceDbContext.Options(Unreachable));
        services.AddScoped<RaceDbContext>();
        if (environment is not null)
            services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment { EnvironmentName = environment });

        return new FakeApplicationBuilder(services.BuildServiceProvider());
    }
}

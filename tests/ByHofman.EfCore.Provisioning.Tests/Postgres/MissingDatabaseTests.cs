using ByHofman.EfCore.Provisioning.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ByHofman.EfCore.Provisioning.Tests.Postgres;

/// <summary>
/// The app's database need not exist yet: <c>MigrateAsync</c> creates it, and apps rely on that (platform's
/// API tests start on a server with no database of theirs). The migration lock lives in that database, so
/// 1.0.2 failed with "database does not exist" before it ever reached <c>MigrateAsync</c>.
/// </summary>
public sealed class MissingDatabaseTests : IAsyncLifetime
{
    private const int Replicas = 6;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigratingIntoADatabaseThatDoesNotExistYet_CreatesIt()
    {
        var address = Address("contoso_new");

        await Replica(address).ProvisionAsync<RaceDbContext>();

        await using var check = new RaceDbContext(RaceDbContext.Options(address));
        (await check.Database.GetAppliedMigrationsAsync()).Should().Equal(check.Database.GetMigrations());
    }

    [Fact]
    public async Task ReplicasStartingTogetherWithNoDatabase_AllProvisionIt()
    {
        var address = Address("fabrikam_new");
        using var start = new Barrier(Replicas);

        await Task.WhenAll(Enumerable.Range(0, Replicas).Select(i => Task.Run(async () =>
        {
            var app = Replica($"{address};Application Name=replica-{i}");
            start.SignalAndWait();
            await app.ProvisionAsync<RaceDbContext>();
        })));

        await using var check = new RaceDbContext(RaceDbContext.Options(address));
        (await check.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyCheckingMigrations_LeavesAMissingDatabaseMissing()
    {
        var address = Address("northwind_new");

        await Replica(address).ProvisionAsync<RaceDbContext>(options =>
        {
            options.CheckMigrations = true;
            options.ApplyMigrations = false;
        });

        await using var check = new RaceDbContext(RaceDbContext.Options(address));
        (await check.Database.GetService<IRelationalDatabaseCreator>().ExistsAsync()).Should().BeFalse();
    }

    private string Address(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    private static FakeApplicationBuilder Replica(string address)
    {
        var services = new ServiceCollection();
        services.AddSingleton(RaceDbContext.Options(address));
        services.AddScoped<RaceDbContext>();

        return new FakeApplicationBuilder(services.BuildServiceProvider());
    }
}

using ByHofman.EfCore.Provisioning.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ByHofman.EfCore.Provisioning.Tests.Postgres;

/// <summary>
/// Replicas of one app start together on an empty database (stories, 2026-11-12: one pod failed with
/// "already exists" and restarted). Each replica must come up, and the schema must be built once.
/// </summary>
public sealed class ConcurrentProvisioningTests : IAsyncLifetime
{
    private const int Replicas = 6;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ReplicasStartingTogetherOnAnEmptyDatabase_AllProvision_AndSeedOnce()
    {
        var seeded = 0;
        using var start = new Barrier(Replicas);

        var replicas = Enumerable.Range(0, Replicas).Select(_ => Task.Run(async () =>
        {
            var app = Replica();
            start.SignalAndWait();
            await app.ProvisionAsync<RaceDbContext>(options => options.Seeder = async (db, _, ct) =>
            {
                var empty = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM notebooks").SingleAsync(ct) == 0;
                if (!empty)
                    return;

                Interlocked.Increment(ref seeded);
                await db.Database.ExecuteSqlRawAsync("INSERT INTO notebooks (id, title) VALUES (gen_random_uuid(), 'Contoso')", ct);
            });
        }));

        await Task.WhenAll(replicas);

        await using var check = new RaceDbContext(RaceDbContext.Options(_postgres.GetConnectionString()));
        (await check.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await check.Database.GetAppliedMigrationsAsync()).Should().Equal(check.Database.GetMigrations());
        seeded.Should().Be(1);
    }

    [Fact]
    public async Task TheLockIsLetGo_SoTheNextReplicaFindsNothingToDo()
    {
        await Replica("first").ProvisionAsync<RaceDbContext>();

        // Its own pool, so it cannot borrow the first replica's session and with it the lock.
        var next = Replica("next").ProvisionAsync<RaceDbContext>();

        (await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(30)))).Should().BeSameAs(next);
        await next;
    }

    private FakeApplicationBuilder Replica(string name = "replica")
    {
        var services = new ServiceCollection();
        services.AddSingleton(RaceDbContext.Options($"{_postgres.GetConnectionString()};Application Name={name}"));
        services.AddScoped<RaceDbContext>();

        return new FakeApplicationBuilder(services.BuildServiceProvider());
    }
}

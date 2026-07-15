using FluentAssertions;
using ByHofman.EfCore.Provisioning.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByHofman.EfCore.Provisioning.Tests;

public sealed class ProvisionAllAsyncTests
{
    private static (FakeApplicationBuilder App, SqliteConnection C1, SqliteConnection C2) BuildApp(
        Action<IServiceCollection>? configure = null)
    {
        var c1 = new SqliteConnection("DataSource=:memory:");
        var c2 = new SqliteConnection("DataSource=:memory:");
        c1.Open();
        c2.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseSqlite(c1));
        services.AddDbContext<SecondTestDbContext>(o => o.UseSqlite(c2));
        configure?.Invoke(services);

        return (new FakeApplicationBuilder(services.BuildServiceProvider()), c1, c2);
    }

    [Fact]
    public async Task ProvisionsBothContexts_WhenTwoEntriesProvided()
    {
        var (app, c1, c2) = BuildApp();
        using (c1) using (c2)
        {
            var firstCalled = false;
            var secondCalled = false;

            await app.ProvisionAllAsync(
                EfProvisioningEntry.For<TestDbContext>(o =>
                    o.Seeder = (db, sp, ct) => { firstCalled = true; return Task.CompletedTask; }),
                EfProvisioningEntry.For<SecondTestDbContext>(o =>
                    o.Seeder = (db, sp, ct) => { secondCalled = true; return Task.CompletedTask; })
            );

            firstCalled.Should().BeTrue();
            secondCalled.Should().BeTrue();
        }
    }

    [Fact]
    public async Task EachContextUsesItsOwnOptions()
    {
        var logger1 = new CapturingLogger<TestDbContext>();
        var logger2 = new CapturingLogger<SecondTestDbContext>();

        var (app, c1, c2) = BuildApp(s =>
        {
            s.AddSingleton<ILogger<TestDbContext>>(logger1);
            s.AddSingleton<ILogger<SecondTestDbContext>>(logger2);
        });
        using (c1) using (c2)
        {
            await app.ProvisionAllAsync(
                EfProvisioningEntry.For<TestDbContext>(o => o.ApplyMigrations = true),
                EfProvisioningEntry.For<SecondTestDbContext>(o => o.ApplyMigrations = false)
            );

            logger1.Entries.Should().Contain(e => e.Message.Contains("Applying migrations"));
            logger2.Entries.Should().NotContain(e => e.Message.Contains("Applying migrations"));
        }
    }

    [Fact]
    public async Task ProvisionsBothContexts_WithCancellationToken()
    {
        var (app, c1, c2) = BuildApp();
        using (c1) using (c2)
        {
            var act = () => app.ProvisionAllAsync(
                CancellationToken.None,
                EfProvisioningEntry.For<TestDbContext>(),
                EfProvisioningEntry.For<SecondTestDbContext>()
            );

            await act.Should().NotThrowAsync();
        }
    }
}

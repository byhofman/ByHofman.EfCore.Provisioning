using FluentAssertions;
using ByHofman.EfCore.Provisioning.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ByHofman.EfCore.Provisioning.Tests;

public sealed class ProvisionAsyncTests
{
    // Builds a minimal IApplicationBuilder backed by a fresh in-memory SQLite database.
    // Returns the app and a connection that must be kept open for the lifetime of the test.
    private static (FakeApplicationBuilder App, SqliteConnection Connection) BuildApp(
        Action<IServiceCollection>? configure = null,
        IConfiguration? configuration = null,
        string? environmentName = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseSqlite(connection));

        if (configuration is not null)
            services.AddSingleton(configuration);

        if (environmentName is not null)
            services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment { EnvironmentName = environmentName });

        configure?.Invoke(services);

        return (new FakeApplicationBuilder(services.BuildServiceProvider()), connection);
    }

    [Fact]
    public async Task CompletesWithoutException_WithDefaultOptions()
    {
        var (app, connection) = BuildApp();
        using (connection)
        {
            var act = () => app.ProvisionAsync<TestDbContext>();
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task CallsSeeder_WhenSeederIsConfigured()
    {
        var (app, connection) = BuildApp();
        using (connection)
        {
            var called = false;

            await app.ProvisionAsync<TestDbContext>(o =>
                o.Seeder = (db, sp, ct) => { called = true; return Task.CompletedTask; });

            called.Should().BeTrue();
        }
    }

    [Fact]
    public async Task SeederReceivesTypedDbContext()
    {
        var (app, connection) = BuildApp();
        using (connection)
        {
            DbContext? received = null;

            await app.ProvisionAsync<TestDbContext>(o =>
                o.Seeder = (db, sp, ct) => { received = db; return Task.CompletedTask; });

            received.Should().BeOfType<TestDbContext>();
        }
    }

    [Fact]
    public async Task LogsNoPendingMigrations_WhenCheckMigrationsIsEnabled()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var (app, connection) = BuildApp(s => s.AddSingleton<ILogger<TestDbContext>>(logger));
        using (connection)
        {
            await app.ProvisionAsync<TestDbContext>(o => o.CheckMigrations = true);

            logger.Entries.Should().Contain(e => e.Message.Contains("No pending migrations"));
        }
    }

    [Fact]
    public async Task DoesNotLogMigrationInfo_WhenCheckMigrationsIsDisabled()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var (app, connection) = BuildApp(s => s.AddSingleton<ILogger<TestDbContext>>(logger));
        using (connection)
        {
            await app.ProvisionAsync<TestDbContext>(o => o.CheckMigrations = false);

            logger.Entries.Should().NotContain(e => e.Message.Contains("pending migration"));
        }
    }

    [Fact]
    public async Task LogsMigrationApply_WhenApplyMigrationsIsEnabled()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var (app, connection) = BuildApp(s => s.AddSingleton<ILogger<TestDbContext>>(logger));
        using (connection)
        {
            await app.ProvisionAsync<TestDbContext>(o => o.ApplyMigrations = true);

            logger.Entries.Should().Contain(e => e.Message.Contains("Applying migrations"));
        }
    }

    [Fact]
    public async Task DoesNotLogMigrationApply_WhenApplyMigrationsIsDisabled()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var (app, connection) = BuildApp(s => s.AddSingleton<ILogger<TestDbContext>>(logger));
        using (connection)
        {
            await app.ProvisionAsync<TestDbContext>(o => o.ApplyMigrations = false);

            logger.Entries.Should().NotContain(e => e.Message.Contains("Applying migrations"));
        }
    }

    [Fact]
    public async Task BindsOptionsFromConfiguration()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EfProvisioning:ApplyMigrations"] = "false"
            })
            .Build();

        var (app, connection) = BuildApp(
            s => s.AddSingleton<ILogger<TestDbContext>>(logger),
            config);
        using (connection)
        {
            await app.ProvisionAsync<TestDbContext>();

            logger.Entries.Should().NotContain(e => e.Message.Contains("Applying migrations"));
        }
    }

    [Fact]
    public async Task InlineLambdaOverridesConfiguration()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EfProvisioning:ApplyMigrations"] = "false"
            })
            .Build();

        var (app, connection) = BuildApp(
            s => s.AddSingleton<ILogger<TestDbContext>>(logger),
            config);
        using (connection)
        {
            // inline lambda sets it back to true — should win over config
            await app.ProvisionAsync<TestDbContext>(o => o.ApplyMigrations = true);

            logger.Entries.Should().Contain(e => e.Message.Contains("Applying migrations"));
        }
    }

    [Fact]
    public async Task ThrowsAfterMaxRetries_WhenAllAttemptsExhausted()
    {
        var (app, connection) = BuildApp();
        using (connection)
        {
            var failingApp = new FakeApplicationBuilder(
                new FailingServiceProvider(app.ApplicationServices, failCount: 5));

            var act = () => failingApp.ProvisionAsync<TestDbContext>(o =>
            {
                o.MaxRetries = 2;
                o.RetryDelay = TimeSpan.Zero;
            });

            await act.Should().ThrowAsync<Exception>();
        }
    }

    [Fact]
    public async Task SucceedsAfterRetry_WhenDatabaseBecomesAvailable()
    {
        var (app, connection) = BuildApp();
        using (connection)
        {
            // fails twice, succeeds on the third attempt (attempt 0, 1 fail → attempt 2 succeeds, MaxRetries=3)
            var failingApp = new FakeApplicationBuilder(
                new FailingServiceProvider(app.ApplicationServices, failCount: 2));

            var act = () => failingApp.ProvisionAsync<TestDbContext>(o =>
            {
                o.MaxRetries = 3;
                o.RetryDelay = TimeSpan.Zero;
            });

            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task LogsRetryWarning_WhenAttemptFails()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var (app, connection) = BuildApp(s => s.AddSingleton<ILogger<TestDbContext>>(logger));
        using (connection)
        {
            var failingApp = new FakeApplicationBuilder(
                new FailingServiceProvider(app.ApplicationServices, failCount: 1));

            await failingApp.ProvisionAsync<TestDbContext>(o =>
            {
                o.MaxRetries = 2;
                o.RetryDelay = TimeSpan.Zero;
            });

            logger.Entries.Should().Contain(e =>
                e.Level == LogLevel.Warning && e.Message.Contains("attempt"));
        }
    }

    [Fact]
    public async Task SkipsMigrations_WhenCurrentEnvironmentNotInMigrateOnEnvironments()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var (app, connection) = BuildApp(
            s => s.AddSingleton<ILogger<TestDbContext>>(logger),
            environmentName: "Development");
        using (connection)
        {
            await app.ProvisionAsync<TestDbContext>(o =>
            {
                o.MigrateOnEnvironments = ["Production"];
            });

            logger.Entries.Should().NotContain(e => e.Message.Contains("Applying migrations"));
            logger.Entries.Should().Contain(e => e.Message.Contains("not in MigrateOnEnvironments"));
        }
    }

    [Fact]
    public async Task AppliesMigrations_WhenCurrentEnvironmentInMigrateOnEnvironments()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var (app, connection) = BuildApp(
            s => s.AddSingleton<ILogger<TestDbContext>>(logger),
            environmentName: "Development");
        using (connection)
        {
            await app.ProvisionAsync<TestDbContext>(o =>
            {
                o.MigrateOnEnvironments = ["Development", "Staging"];
            });

            logger.Entries.Should().Contain(e => e.Message.Contains("Applying migrations"));
        }
    }

    [Fact]
    public async Task AppliesMigrations_WhenMigrateOnEnvironmentsIsNull()
    {
        var logger = new CapturingLogger<TestDbContext>();
        var (app, connection) = BuildApp(s => s.AddSingleton<ILogger<TestDbContext>>(logger));
        using (connection)
        {
            await app.ProvisionAsync<TestDbContext>(o =>
            {
                o.MigrateOnEnvironments = null;
            });

            logger.Entries.Should().Contain(e => e.Message.Contains("Applying migrations"));
        }
    }
}

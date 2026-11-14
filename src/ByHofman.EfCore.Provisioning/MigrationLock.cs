using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace ByHofman.EfCore.Provisioning;

/// <summary>
/// Serialises provisioning across processes on PostgreSQL. EF Core's <c>MigrateAsync</c> does not stop two
/// replicas that start together on an empty database from applying the same migration, so one of them fails
/// with "already exists". Holding a session advisory lock while checking, migrating and seeding makes them
/// take turns: the first does the work, the others then find nothing pending.
/// </summary>
/// <remarks>
/// A session lock rather than a transaction lock, because EF commits each migration in its own transaction.
/// It lives on the connection EF migrates on, which therefore stays open, and Postgres releases it if the
/// process dies. Other providers are left as they were.
/// </remarks>
internal sealed class MigrationLock : IAsyncDisposable
{
    internal const string Name = "ByHofman.EfCore.Provisioning";

    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string Lock = "SELECT pg_advisory_lock(hashtextextended('" + Name + "', 0))";
    private const string Unlock = "SELECT pg_advisory_unlock(hashtextextended('" + Name + "', 0))";

    private readonly DatabaseFacade? _held;

    private MigrationLock(DatabaseFacade? held) => _held = held;

    /// <param name="createIfMissing">
    /// Whether migrations will run. The lock lives in the app's own database, so that database has to exist
    /// before it can be taken; <c>MigrateAsync</c> would create it, so it is created here first. Without
    /// migrations a missing database is left alone and there is nothing to serialise.
    /// </param>
    public static async Task<MigrationLock> AcquireAsync(
        DatabaseFacade database, bool createIfMissing, CancellationToken cancellationToken)
    {
        if (database.ProviderName != NpgsqlProvider)
            return new MigrationLock(null);

        var creator = database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
        {
            if (!createIfMissing)
                return new MigrationLock(null);

            try
            {
                await creator.CreateAsync(cancellationToken);
            }
            catch
            {
                // Another replica created it between the check and the CREATE DATABASE.
                if (!await creator.ExistsAsync(cancellationToken))
                    throw;
            }
        }

        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await database.ExecuteSqlRawAsync(Lock, cancellationToken);
        }
        catch
        {
            await database.CloseConnectionAsync();
            throw;
        }

        return new MigrationLock(database);
    }

    public async ValueTask DisposeAsync()
    {
        if (_held is null)
            return;

        try
        {
            await _held.ExecuteSqlRawAsync(Unlock, CancellationToken.None);
        }
        finally
        {
            await _held.CloseConnectionAsync();
        }
    }
}

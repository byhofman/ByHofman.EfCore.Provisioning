# Usage

## Configuration

Options can be set from `appsettings.json` / environment variables, from code, or both. Code always wins over config.

### appsettings.json

The default configuration section name is exposed as `EfProvisioningOptions<TDbContext>.SectionName` (`"EfProvisioning"`):

```json
{
  "EfProvisioning": {
    "CheckMigrations": true,
    "ApplyMigrations": true,
    "MaxRetries": 3,
    "RetryDelay": "00:00:05",
    "MigrateOnEnvironments": ["Development", "Staging"]
  }
}
```

### Environment variables

Uses the standard ASP.NET Core double-underscore separator:

```
EFPROVISIONING__CHECKMIGRATIONS=true
EFPROVISIONING__APPLYMIGRATIONS=true
EFPROVISIONING__MAXRETRIES=3
EFPROVISIONING__RETRYDELAY=00:00:05
EFPROVISIONING__MIGRATEONENVIRONMENTS__0=Development
EFPROVISIONING__MIGRATEONENVIRONMENTS__1=Staging
```

### Custom section key

```csharp
await app.ProvisionAsync<AppDbContext>(configSectionKey: "MyApp:Database");
```

### Inline code

```csharp
await app.ProvisionAsync<AppDbContext>(options =>
{
    options.CheckMigrations = true;
    options.ApplyMigrations = true;
    options.MaxRetries = 5;
    options.RetryDelay = TimeSpan.FromSeconds(3);
});
```

## Options reference

| Option | Type | Default | Description |
|---|---|---|---|
| `CheckMigrations` | `bool` | `true` | Queries the database for pending and applied migrations and logs the results. Set `ApplyMigrations = false` to use this as a dry-run / check-only mode. |
| `ApplyMigrations` | `bool` | `true` | Applies all pending migrations via `Database.MigrateAsync()`. |
| `MigrateOnEnvironments` | `string[]?` | `null` | Restricts migration application to the listed environment names. `null` or empty applies migrations in all environments. See [Environment-gated migrations](#environment-gated-migrations). |
| `MaxRetries` | `int` | `0` | Number of retry attempts when provisioning fails (e.g. database container not yet ready). `0` means fail immediately. |
| `RetryDelay` | `TimeSpan` | `00:00:05` | Delay between retry attempts. |
| `Seeder` | `Func<TDbContext, IServiceProvider, CancellationToken, Task>?` | `null` | Optional delegate called after migrations are applied. See [Seed data](#seed-data). |

## Features

### Wait-for-database (retry)

Useful in containerised environments where the database container may not be reachable when the app starts:

```csharp
await app.ProvisionAsync<AppDbContext>(options =>
{
    options.MaxRetries = 10;
    options.RetryDelay = TimeSpan.FromSeconds(5);
});
```

Each retry creates a fresh service scope and database connection. On final failure the exception is re-thrown and logged at `Error`.

### Seed data

The `Seeder` delegate runs after migrations are applied. It receives the typed `DbContext` and the scoped `IServiceProvider`, so you can resolve additional services:

```csharp
await app.ProvisionAsync<AppDbContext>(options =>
{
    options.Seeder = async (db, sp, ct) =>
    {
        if (!await db.Roles.AnyAsync(ct))
        {
            db.Roles.AddRange(RoleSeeder.DefaultRoles());
            await db.SaveChangesAsync(ct);
        }
    };
});
```

### Dry-run / check-only

Set `ApplyMigrations = false` to log pending migrations without applying them — useful in CI to assert the repository has no unapplied migrations:

```csharp
await app.ProvisionAsync<AppDbContext>(options =>
{
    options.CheckMigrations = true;
    options.ApplyMigrations = false;
});
```

### Environment-gated migrations

Use `MigrateOnEnvironments` to restrict automatic migration application to specific ASP.NET Core environments. The check compares against `IHostEnvironment.EnvironmentName` (case-insensitive). `CheckMigrations` and `Seeder` are unaffected — only `ApplyMigrations` is gated.

```csharp
// Only apply migrations when running in Development or Staging
await app.ProvisionAsync<AppDbContext>(options =>
{
    options.MigrateOnEnvironments = ["Development", "Staging"];
});
```

Via `appsettings.json`:

```json
{
  "EfProvisioning": {
    "MigrateOnEnvironments": ["Development", "Staging"]
  }
}
```

When the current environment is not in the list, a log entry is written at `Information` level and migrations are skipped. The seeder still runs.

### Multiple DbContext

Use `ProvisionAllAsync` with `EfProvisioningEntry.For<TDbContext>()` to provision multiple contexts sequentially, each with their own options:

```csharp
await app.ProvisionAllAsync(
    EfProvisioningEntry.For<AppDbContext>(),
    EfProvisioningEntry.For<LogDbContext>(options =>
    {
        options.ApplyMigrations = false;
    })
);
```

A `CancellationToken` overload is also available:

```csharp
await app.ProvisionAllAsync(
    cancellationToken,
    EfProvisioningEntry.For<AppDbContext>(),
    EfProvisioningEntry.For<LogDbContext>()
);
```

#### Per-context configuration via appsettings

To configure each context independently from `appsettings.json`, pass a unique `configSectionKey` per entry and add matching sections:

```csharp
// Program.cs
await app.ProvisionAllAsync(
    EfProvisioningEntry.For<AppDbContext>(configSectionKey: "EfProvisioning:AppDbContext"),
    EfProvisioningEntry.For<LogDbContext>(configSectionKey: "EfProvisioning:LogDbContext")
);
```

```json
{
  "EfProvisioning": {
    "AppDbContext": {
      "ApplyMigrations": true,
      "MaxRetries": 5,
      "RetryDelay": "00:00:03",
      "MigrateOnEnvironments": ["Development", "Staging"]
    },
    "LogDbContext": {
      "ApplyMigrations": false,
      "CheckMigrations": true
    }
  }
}
```

## Logging

The library logs via `ILogger<TDbContext>`, which integrates with any `Microsoft.Extensions.Logging` provider (Serilog, OpenTelemetry, etc.).

| Event | Level |
|---|---|
| No pending migrations found | `Information` |
| Pending migration names + applied count | `Information` |
| Starting / completed migration apply | `Information` |
| Seeder started / completed | `Information` |
| Retry attempt with exception details | `Warning` |
| Final failure after all retries | `Error` |

Example output with Serilog:

```
[INF] Found 2 pending migration(s) for AppDbContext (5 applied): ["20240101_AddUsers", "20240115_AddRoles"]
[INF] Applying migrations for AppDbContext...
[INF] Migrations applied successfully for AppDbContext
[INF] Running seeder for AppDbContext...
[INF] Seeder completed for AppDbContext
```

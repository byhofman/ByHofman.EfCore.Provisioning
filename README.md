# ByHofman.EfCore.Provisioning

ASP.NET Core extension library for configurable EF Core database provisioning at application startup. Handles migration checks, automatic migration apply, seed data, and wait-for-database retry — with structured logging throughout.

## Installation

The package is published to GitHub Packages. Add the `byhofman` NuGet source once, then install:

```
dotnet nuget add source "https://nuget.pkg.github.com/byhofman/index.json" \
  --name byhofman --username <github-username> --password <github-token>

dotnet add package ByHofman.EfCore.Provisioning
```

> GitHub Packages requires authentication even for public packages. Use a personal access token with the `read:packages` scope.

## Quick start

```csharp
// Program.cs (.NET 6+)
var app = builder.Build();

await app.ProvisionAsync<AppDbContext>();

app.Run();
```

For full configuration options and usage examples, see [docs/Usage.md](docs/Usage.md).

## Requirements

- .NET Standard 2.1 / .NET 5+
- `Microsoft.EntityFrameworkCore.Relational` 5.0+
- `Microsoft.AspNetCore.Http.Abstractions` 2.2+

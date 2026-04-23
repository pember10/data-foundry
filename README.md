# WTW Diffusion

> **Note:** The VSIX project is currently named `data-foundry` for historical reasons. It will be renamed to `WTW.Diffusion.Extension` in a future effort, alongside the migration to `net8.0-windows`.

A Visual Studio 2022 extension for automating SQL Server data change detection, migration script generation, and deployment. Designed to work alongside SQL Server Data Projects (`.sqlproj`).

---

## What It Does

WTW Diffusion keeps your database's reference/seed data in sync with your source-controlled migration scripts. It:

1. **Detects data changes** between your local development database and a shadow database (rebuilt from your migration scripts)
2. **Generates migration scripts** from those differences
3. **Deploys migrations** to a target SQL Server database
4. **Adds generated scripts** directly into your `.sqlproj` so they're tracked in source control

The shadow database is the source of truth -- it represents what the database *should* look like after all migrations have been applied.

---

## Prerequisites

| Requirement | Version |
|---|---|
| Visual Studio | 2022 (17.0+) |
| .NET Framework | 4.7.2 |
| SQL Server | Any edition (LocalDB, Express, Standard, Azure SQL) |
| SQL Server Data Tools | Installed via VS Installer |
| PowerShell SqlServer module | Required only if using PowerShell execution mode |

---

## Solution Structure

```
data-foundry/
└ data-foundry.csproj               # VSIX project -- VS extension shell
   └ Options/                       # Tools > Options settings page
   └ Services/                      # VS-specific services (DTE, Output Window, PowerShell)
      └ Adapters/                   # ILogger, IConfigurationProvider, IProjectManager > VS
   └ Views/Controls/                # WPF tab controls (Overview, Changes, Deployment, Settings)

└ WTW.Diffusion.Core/               # .NET Standard 2.0 class library -- all business logic
    └ Abstractions/                 # ILogger, IConfigurationProvider, IProjectManager
    └ Config/                       # tablelist.json and DataFoundryConfig loader
    └ Models/                       # MigrationInfo, TableChangeSummary, ActivityEntry, etc.
    └ Orchestration/                # IOrchestratorStep, OrchestratorContext (future designer)
    └ Services/
        └ Database/                 # SqlMigrationRepository, ChangeDetectionService, AzureSqlAuthenticationProvider
        └ Migration/                # MigrationScriptManager, MigrationScriptGenerator, IMigrationExecutor
        └ ShadowDatabaseManager.cs

└ WTW.Diffusion.Cli/                # .NET 8 console app -- CI/CD pipeline tool
    └ Adapters/                     # ConsoleLogger, FileSystemProjectManager
    └ Commands/                     # init subcommand
    └ Infrastructure/               # ServiceContext -- wires Core services from CLI params
    └ Program.cs                    # Root command; mirrors SqlMetadataAutomation.ps1 params
```

The `WTW.Diffusion.Core` library has **zero dependencies on Visual Studio** -- no DTE, no WPF, no VS SDK. This separation exists so the same business logic can be reused in a future CLI tool for Azure DevOps / GitHub Actions pipelines.

---


## Architecture

```
┌─────────────────────────────────┐    ┌──────────────────────────────────┐
│     data-foundry (VSIX)         │    │        WTW.Diffusion.Cli         │
│     .NET Framework 4.7.2        │    │        .NET 8                    │
│                                 │    │                                  │
│  VsLogger                       │    │  ConsoleLogger                   │
│  VsConfigurationProvider        │    │  FileSystemProjectManager        │
│  VsProjectManager               │    │  PS-equivalent CLI params        │
│  WPF UI, DTE, VS SDK            │    │  init subcommand                 │
└──────────────┬──────────────────┘    └───────────────┬──────────────────┘
               │                                       │
               └──────────────┬────────────────────────┘
                              │
               ┌──────────────┴───────────┐
               │   WTW.Diffusion.Core     │
               │   .NET Standard 2.0      │
               │                          │
               │  ShadowDatabaseManager   │
               │  ChangeDetectionService  │
               │  MigrationScriptManager  │
               │  MigrationScriptGenerator│
               │  SqlMigrationRepository  │
               └──────────────────────────┘
```

---

## Key Concepts

### Shadow Database
A temporary database rebuilt from scratch by replaying all migration scripts in order. It represents the "known good" state of the database. WTW Diffusion compares your target database against this shadow to find data drift.

The shadow database is recreated only when migration files change -- detected via a SHA256 hash of all `.sql` filenames and their last-write timestamps. The hash is cached in memory and persisted to `shadow-cache.json` to survive extension restarts.

### Migration Log Table
Every executed script is recorded in `dbo.__MigrationLog`. Each script embeds a GUID in a comment header:

```sql
-- <Migration ID="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" />
```

Scripts are never re-executed if their GUID is already in the log. Script execution and log writes are **atomic** -- wrapped in a single transaction so a failed script never gets marked as applied.

### Change Detection
Changes are detected by comparing the target database against the shadow using `HASHBYTES('SHA2_256', ...)` per row. `CHECKSUM()` is intentionally avoided due to collision risk.

Inserts, updates, and deletes are detected separately. Only tables listed in `Config/tablelist.json` are tracked.

### Dual Execution Mode
Operations can run via:
- **C# mode** (default) -- direct SQL execution using `SqlMigrationRepository`
- **PowerShell mode** -- delegates to `SqlMetadataAutomation.ps1` via `PowerShellScriptRunner`

Toggle via **Tools > Options > WTW Diffusion > Advanced > Use PowerShell Script**.

---

## Configuration

All settings are under **Tools > Options > WTW Diffusion**.

| Setting | Description |
|---|---|
| Local Database Connection | Connection string to your development database |
| Shadow Database Connection | Optional -- derived from local connection if not set |
| SQL Project | The `.sqlproj` to add generated scripts into |
| Migrations Folder | Folder within the SQL project containing migration scripts |
| Tracked Tables | Comma-separated list of tables to watch for data changes |
| Use PowerShell Script | Switch between C# and PowerShell execution modes |
| Verbose Logging | Enables debug-level output in the Output window |

Tracked tables are stored in `Config/tablelist.json` in the extension's install directory and can also be edited directly.

---

## CLI Usage

The CLI (`wtw-diffusion`) is a drop-in replacement for `SqlMetadataAutomation.ps1` for use in Azure DevOps / GitHub Actions pipelines. It accepts the same parameters, so existing pipeline definitions need minimal changes.

### Parameters

| Parameter | Required | Description |
|---|---|---|
| `--target-database` | Y | Target database name |
| `--target-server` | Y | SQL Server instance or hostname |
| `--migrations-path` | Y | Path containing migration `.sql` files |
| `--confirm-target-migration` | | Prompt before applying pending migrations |
| `--detect-changes` | | Recreate shadow DB and compare tracked tables |
| `--action` | | `Revert` \| `Migrate` \| `Cancel` (prompts if omitted with `--detect-changes`) |
| `--config-path` | | Path to `config.json` with `TrackedTables` array. Defaults to `./config.json` |
| `--output-migration-dir` | | Output folder for generated scripts. Defaults to `--migrations-path` |
| `--script-name` | | Generated script name (prompts if omitted with `--action Migrate`) |
| `--shadow-database` | | Shadow DB name. Defaults to `{TargetDatabase}_Shadow` |
| `--migration-log-schema` | | Path to `MigrationLogTableDefinition.sql`. Defaults to file beside executable |

All options also accept their original PascalCase forms (e.g. `--TargetDatabase`) so existing pipeline scripts can swap `SqlMetadataAutomation.ps1` for `wtw-diffusion` with no argument changes.

### config.json

When using `--detect-changes`, point `--config-path` at a JSON file with a `TrackedTables` array — the same format the PowerShell script uses:

```json
{
  "TrackedTables": [ "dbo.MyTable", "dbo.AnotherTable" ]
}
```

Scaffold a starter file with:

```bash
wtw-diffusion init --output ./
```

### Examples

```bash
# Apply pending migrations only (equivalent to running the PS script without -DetectChanges)
wtw-diffusion --target-database AppDb --target-server .\SQLEXPRESS --migrations-path ./Migrations

# Apply migrations then detect and prompt for action interactively
wtw-diffusion --target-database AppDb --target-server .\SQLEXPRESS --migrations-path ./Migrations \
  --detect-changes --config-path ./config.json

# Fully automated: apply migrations, detect changes, generate a named script
wtw-diffusion --target-database AppDb --target-server .\SQLEXPRESS --migrations-path ./Migrations \
  --detect-changes --action Migrate --script-name 001_US123456_SeedData \
  --config-path ./config.json

# Azure SQL (token acquired automatically via DefaultAzureCredential)
wtw-diffusion --target-database AppDb --target-server myserver.database.windows.net \
  --migrations-path ./Migrations --detect-changes --action Migrate --script-name MySeed \
  --config-path ./config.json
```

---

## Building

The VSIX **must be built through Visual Studio** -- `dotnet build` and direct `MSBuild.exe` invocations will fail due to WPF/WinFx targets requiring the VS toolchain.

`WTW.Diffusion.Core` can be built independently:

```bash
dotnet build WTW.Diffusion.Core/WTW.Diffusion.Core.csproj
```

If ghost CS2001 errors appear for files that no longer exist, delete `obj\` and rebuild.

### Key Package Constraints

- **`System.Management.Automation`** is pinned at `5.1.1`. The `6.x` and `7.x` releases target `netcoreapp2.1` and `net8.0` respectively -- neither is compatible with `.NET Framework 4.7.2`. Upgrading requires migrating the VSIX to `net8.0-windows` (VS 2022 17.9+).
- **`Microsoft.Data.SqlClient`** is used in `WTW.Diffusion.Core`. The VSIX orchestrator uses the legacy `System.Data.SqlClient` for compatibility.

---

## Azure SQL Support

Azure SQL is supported. The extension detects Azure SQL endpoints (`.database.windows.net`) and acquires an access token via `AzureSqlAuthenticationProvider` using `DefaultAzureCredential` from `Azure.Identity`.

Database creation is skipped for Azure SQL targets since it requires elevated permissions not typically available in development scenarios.

---

## Roadmap

- [x] **`WTW.Diffusion.Cli`** -- `net8.0` console app referencing `WTW.Diffusion.Core`. Drop-in replacement for `SqlMetadataAutomation.ps1` with identical parameters. Supports `--detect-changes`, `--action Migrate/Revert/Cancel`, and an `init` subcommand for scaffolding `config.json`.
- [ ] **Wire adapter abstractions** -- Replace `Action<string>` logger pattern throughout Core with `ILogger`. `VsLogger`, `VsConfigurationProvider`, and `VsProjectManager` adapters are already implemented in the VSIX.
- [ ] **Migrate VSIX to `net8.0-windows`** -- Unlocks newer `System.Management.Automation` and SDK-style project format. Planned alongside the CLI effort.
- [ ] **`CancellationToken` support** -- Pass cancellation through all async operations.
- [ ] **UI/UX polish** -- Spacing, alignment, grid column sizing.

---

## Technologies

| Technology | Version | Used In |
|---|---|---|
| .NET Framework | 4.7.2 | VSIX |
| .NET Standard | 2.0 | Core library |
| Visual Studio SDK | 17.0 | VSIX |
| WPF | -- | VSIX UI |
| Microsoft.Data.SqlClient | 5.1.5 | Core |
| Azure.Identity | 1.21.0 | Core |
| Azure.Core | 1.53.0 | Core |
| Microsoft.Identity.Client | 4.83.3 | Core |
| Newtonsoft.Json | 13.0.3 | Core |
| System.Management.Automation | 5.1.1 | VSIX |
| System.CommandLine | 2.0.0-beta4 | CLI |

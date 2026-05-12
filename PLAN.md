# WTW Diffusion — Project Plan

## Overview

WTW Diffusion is a developer productivity tool for teams working with SQL Server databases alongside
source-controlled migration scripts. It automates the detection, scripting, and deployment of data
changes so that seed/reference data stays in sync with the rest of the codebase.

The tool is delivered in two forms:

- A **Visual Studio 2022 extension (VSIX)** for interactive, developer-facing workflows
- A **cross-platform CLI** (`wtw-diffusion`) for unattended use in CI/CD pipelines

---

## Problem Statement

SQL Server Data Projects (`.sqlproj`) handle schema migrations well, but they have no built-in
concept of **data migrations** — changes to seed or reference tables that must be scripted,
reviewed, and deployed alongside schema changes.

Without tooling, developers must:

1. Manually compare their local database against the expected baseline
2. Hand-write `INSERT` / `UPDATE` / `DELETE` scripts
3. Remember to add those scripts to the project and commit them
4. Re-run the baseline on every environment that needs to catch up

WTW Diffusion automates all of this.

---

## Core Concept — The Shadow Database

The shadow database is the heart of the system. It is a throw-away database that is rebuilt from
scratch by replaying every migration script in the repository in order. It represents what the
database **should** look like — the source of truth.

Change detection works by comparing each tracked table in the **target** (developer's local
database) against the corresponding table in the **shadow**. Any row that differs represents a
data change that needs to be captured as a migration script.

```
Migrations folder
       ?
       ?
Shadow DB  ??? rebuilt from all scripts (source of truth)
       ?
       ?  compare tracked tables
       ?
Target DB  ??? developer's working database
       ?
       ?
  Differences
  ??? New rows   ? INSERT statements
  ??? Changed rows ? UPDATE statements
  ??? Deleted rows ? DELETE statements
```

---

## Solution Structure

```
data-foundry.sln
??? data-foundry/               # VSIX — .NET Framework 4.7.2
??? WTW.Diffusion.Core/         # Business logic — .NET Standard 2.0
??? WTW.Diffusion.Cli/          # CLI tool — .NET 8
??? WTW.Diffusion.Tests/        # Core + CLI unit tests — .NET 8
??? WTW.Diffusion.Vsix.Tests/   # VSIX service tests — .NET Framework 4.7.2
```

### Why three separate projects?

| Project | Reason for separation |
|---|---|
| `WTW.Diffusion.Core` | No VS dependency — reusable in CLI and testable with `dotnet test` |
| `data-foundry` (VSIX) | Requires VS SDK, DTE, WPF — cannot be used outside Visual Studio |
| `WTW.Diffusion.Cli` | Requires .NET 8 for cross-platform publish and modern SDK features |

---

## Architecture

```
?????????????????????????????   ?????????????????????????????????
?    data-foundry (VSIX)    ?   ?      WTW.Diffusion.Cli        ?
?    .NET Framework 4.7.2   ?   ?      .NET 8                   ?
?                           ?   ?                               ?
?  VsLogger                 ?   ?  ConsoleLogger                ?
?  VsConfigurationProvider  ?   ?  FileConfigurationProvider    ?
?  VsProjectManager         ?   ?  FileSystemProjectManager     ?
?  WPF UI / DTE / VS SDK    ?   ?  AzdoLogger (--azdo mode)     ?
?????????????????????????????   ?????????????????????????????????
             ?                                  ?
             ????????????????????????????????????
                              ?
             ??????????????????????????????????????
             ?       WTW.Diffusion.Core            ?
             ?       .NET Standard 2.0             ?
             ?                                    ?
             ?  SqlMigrationRepository             ?
             ?  ChangeDetectionService             ?
             ?  MigrationScriptGenerator           ?
             ?  MigrationScriptManager             ?
             ?  ShadowDatabaseManager              ?
             ?  AzureSqlAuthenticationProvider     ?
             ??????????????????????????????????????
```

### Abstractions (defined in Core, implemented per host)

| Interface | VSIX implementation | CLI implementation |
|---|---|---|
| `ILogger` | `VsLogger` (Output Window) | `ConsoleLogger` / `AzdoLogger` |
| `IConfigurationProvider` | `VsConfigurationProvider` | _(reads CLI args)_ |
| `IProjectManager` | `VsProjectManager` | `FileSystemProjectManager` |

---

## Key Workflows

### 1. Apply pending migrations

Scans the migrations folder for `.sql` files whose GUID (`-- <Migration ID="{guid}" />`) is not yet
recorded in `dbo.__MigrationLog` on the target database. Executes each in filename order.
The script execution and the log write happen in a **single transaction** — either both succeed or
neither does.

### 2. Detect data changes

1. Ensures the shadow database is up to date (rebuilds if the migration hash has changed)
2. Compares each tracked table using `HASHBYTES('SHA2_256', ...)` row hashing
3. Returns a `TableChangeSummary` per table with insert / update / delete counts

### 3. Generate a migration script

Generates `INSERT`, `UPDATE`, and `DELETE` statements for each changed row. Embeds a new GUID as a
migration ID comment so the script can later be logged without re-execution. Adds the script to the
`.sqlproj` via the `IProjectManager` abstraction.

### 4. Revert changes

Replays the shadow database state back onto the target by generating and immediately executing a
revert script. The target is left in the state the shadow defines.

---

## Data Flow

```
Developer edits reference data in their local DB
           ?
           ?
  [ Refresh Changes ]
           ?
           ??? Rebuild shadow DB if migrations changed (SHA-256 hash cache)
           ?         ??? Drop & recreate ? apply all scripts in order
           ?
           ??? Compare tracked tables (configured in tablelist.json)
           ?         ??? HASHBYTES SHA-256 per row ? diff counts per table
           ?
           ??? Show results in Changes tab
                     ?
          ???????????????????????
          ?          ?          ?
       Migrate     Revert     Cancel
          ?          ?
          ?          ?
   Generate SQL  Replay shadow
   script file   ? target DB
          ?
          ?
   Add to .sqlproj
   Commit to source control
```

---

## Database Conventions

### Migration log table — `dbo.__MigrationLog`

Every migration script contains a GUID comment header:

```sql
-- <Migration ID="{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}" />
```

When a script is applied, a row is written to `__MigrationLog` in the same transaction:

| Column | Type | Description |
|---|---|---|
| `Id` | `uniqueidentifier` | The migration GUID from the script header |
| `FileName` | `nvarchar(500)` | Basename of the script file |
| `AppliedAt` | `datetime2` | UTC timestamp of application |
| `Checksum` | `nvarchar(64)` | SHA-256 of the script content |

Scripts whose ID already exists in `__MigrationLog` are **never re-executed**.

### Change detection hashing

Row comparison uses `HASHBYTES('SHA2_256', ...)` over all non-PK columns with
`ISNULL(CONVERT(NVARCHAR(MAX), col), '#NULL#')` to handle nulls deterministically.
`CHECKSUM()` is explicitly avoided — it has well-known collision cases.

### Shadow database caching

Rebuilding the shadow is expensive. A two-level cache avoids unnecessary rebuilds:

1. **In-memory** — a `static` hash string on `ShadowDatabaseManager`
2. **On-disk** — `shadow-cache.json` beside the extension config

The cache key is a SHA-256 over all migration filenames concatenated with their
`LastWriteTimeUtc` ticks. Any file addition, deletion, or modification invalidates it.

---

## CI/CD Integration (`wtw-diffusion` CLI)

The CLI is a drop-in replacement for `SqlMetadataAutomation.ps1`. All parameter names are aliased
to match the PowerShell script exactly, so existing pipeline definitions need no changes.

### Azure DevOps — `--azdo` flag

When `--azdo` is passed:

- Errors emit `##[error]` logging commands (shown red in the ADO log viewer)
- These pipeline variables are set for downstream steps:

| Variable | Example |
|---|---|
| `wtw.hasChanges` | `true` |
| `wtw.pendingMigrations` | `3` |
| `wtw.changedTables` | `dbo.Users,dbo.Orders` |
| `wtw.totalChanges` | `14` |
| `wtw.generatedScript` | `C:\agent\...\001_Seed.sql` |

- A Markdown summary tab is uploaded to the pipeline run page

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Cancelled by user (`--confirm-target-migration` rejected) |
| `2` | Unhandled error |

---

## Configuration

### `tablelist.json` — tracked tables

Located in the extension install directory (VSIX) or passed via `--config-path` (CLI).

```json
{
  "TrackedTables": [
    "dbo.LookupCodes",
    "dbo.Roles",
    "dbo.Permissions"
  ]
}
```

Editable in the VSIX via **Tools ? Options ? WTW Diffusion ? Tracked Tables**.

---

## Testing Strategy

| Project | Framework | What it tests |
|---|---|---|
| `WTW.Diffusion.Tests` | xUnit / net8 | Core parsing, SQL identifier safety, path utilities, script generation (mocked repo), CLI pipeline output |
| `WTW.Diffusion.Vsix.Tests` | xUnit / net472 | VSIX-specific services with no VS dependency: connection string parsing, processing state, event services |

VS-coupled code (DTE, Output Window, WPF) is not unit tested — those are covered by manual
end-to-end testing in the experimental VS instance.

---

## Technology Choices

| Decision | Choice | Reason |
|---|---|---|
| Core target framework | .NET Standard 2.0 | Consumed by both net472 VSIX and net8 CLI |
| VSIX target framework | .NET Framework 4.7.2 | Required by VS 2022 extension model |
| CLI target framework | .NET 8 | Cross-platform publish, self-contained binary |
| SQL client (Core) | `Microsoft.Data.SqlClient` | Active development, Azure AD auth support |
| SQL client (VSIX) | `System.Data.SqlClient` | Legacy compat with net472 VS toolchain |
| JSON serialisation | `Newtonsoft.Json` | Consistent across all three projects |
| CLI argument parsing | `System.CommandLine` (beta4) | Double-dash conventions, built-in `--help` |
| Change detection | `HASHBYTES('SHA2_256', ...)` | Collision-resistant; `CHECKSUM()` is not |
| Test framework | xUnit + FluentAssertions + Moq | Standard modern .NET test stack |
| Azure auth | `Azure.Identity` `DefaultAzureCredential` | Works for local dev, MI, service principal |

---

## Known Constraints

- **VSIX must be built through Visual Studio** — `dotnet build` cannot resolve VS SDK targets
- **`System.Management.Automation` is pinned at 5.1.1** — v6+ targets netcoreapp, v7+ targets
  net8; neither is compatible with net472. Upgrading requires migrating the VSIX to
  `net8.0-windows` (SDK-style, VS 2022 17.9+)
- **`EnvDTE.Constants` namespace conflict** — never use `using static WTW.Diffusion.Core.Constants`
  in VSIX files; use the alias `using CoreConstants = WTW.Diffusion.Core.Constants` instead
- **`DataTable.AsEnumerable()`** is not available in .NET Standard 2.0 — use
  `Rows.Cast<DataRow>()` instead
- **COM interop (`ProjectItems`)** has no empty constructor — `null` is the correct "not found"
  sentinel; suppress SonarQube S1168 with `[SuppressMessage]`

---

## Future Work

| Item | Priority | Notes |
|---|---|---|
| Rename VSIX to `WTW.Diffusion.Extension` | Low | Requires solution restructure |
| Migrate VSIX to `net8.0-windows` SDK-style | Low | Unblocks PowerShell 7, async improvements |
| Wire `IConfigurationProvider` / `IProjectManager` into `SqlMigrationOrchestrator` | Medium | Currently accesses config/project directly |
| `CancellationToken` support in Core services | Medium | Cancel button exists in UI but doesn't interrupt mid-operation |
| Publish `wtw-diffusion` to an internal NuGet feed | Medium | Enables `dotnet tool install` in pipelines |
| GitHub Actions workflow template | Low | Parallel to the ADO example in the CLI README |
| `detect-changes` / `generate-script` / `deploy` as CLI subcommands | Low | Currently all in one root command |

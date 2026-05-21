# WTW Diffusion — Project Plan

## Overview

WTW Diffusion is a developer productivity tool for teams working with SQL Server databases alongside
source-controlled migration scripts. It automates the detection, scripting, and deployment of **data**
changes so that seed/reference data stays in sync with the rest of the codebase.

The tool is delivered in two forms:

- A **Visual Studio 2022/2026 extension (VSIX)** for interactive, developer-facing workflows
- A **cross-platform CLI** (`wtw-diffusion`) for unattended use in CI/CD pipelines

---

## Problem Statement

SQL Server Data Projects (`.sqlproj`) handle schema migrations well, but they have no built-in
concept of **data migrations** -- changes to seed or reference tables that must be scripted,
reviewed, and deployed alongside schema changes.

Without tooling, developers must:

1. Manually compare their local database against the expected baseline
2. Hand-write `INSERT` / `UPDATE` / `DELETE` scripts
3. Remember to add those scripts to the project and commit them
4. Re-run the baseline on every environment that needs to catch up

WTW Diffusion automates all of this.

---

## Core Concept -- The Shadow Database

The shadow database is a throw-away SQL Server database rebuilt from scratch by replaying every
migration script in the repository in order. It represents what the database **should** look like.

Change detection compares each tracked table in the **target** (developer's local database) against
the corresponding table in the **shadow**. Any row that differs must be captured as a new migration.

    Migrations folder
           |
           v
    Shadow DB  <-- rebuilt from all scripts (source of truth)
           |
           |  compare tracked tables
           |
    Target DB  <-- developer's working database
           |
           v
      Differences
      > New rows     -> INSERT statements
      > Changed rows -> UPDATE statements
      > Deleted rows -> DELETE statements

---

## Solution Structure

    data-foundry.sln
    +-- data-foundry/             # VSIX -- .NET Framework 4.7.2
    +-- WTW.Diffusion.Core/       # Business logic -- .NET Standard 2.0
    +-- WTW.Diffusion.Cli/        # CLI tool -- .NET 8
    +-- WTW.Diffusion.Tests/      # Core + CLI unit tests -- .NET 8
    +-- WTW.Diffusion.Vsix.Tests/ # VSIX service tests -- .NET Framework 4.7.2

### Why this structure?

| Project | Framework | Reason |
|---|---|---|
| `WTW.Diffusion.Core` | .NET Standard 2.0 | No VS dependency -- reusable in CLI and testable with `dotnet test` |
| `data-foundry` (VSIX) | .NET Framework 4.7.2 | VS SDK, DTE, WPF -- cannot run outside Visual Studio |
| `WTW.Diffusion.Cli` | .NET 8 | Cross-platform publish, self-contained binary |
| `WTW.Diffusion.Tests` | .NET 8 | xUnit tests for Core and CLI -- runs with `dotnet test` |
| `WTW.Diffusion.Vsix.Tests` | .NET Framework 4.7.2 | xUnit tests for VS-independent VSIX services |

---

## Architecture

    ┌────────────────────────────────┐    ┌──────────────────────────────────┐
    │     data-foundry (VSIX)        │    │       WTW.Diffusion.Cli          │
    │     .NET Framework 4.7.2       │    │       .NET 8                     │
    │                                │    │                                  │
    │  VsLogger                      │    │  ConsoleLogger                   │
    │  VsConfigurationProvider       │    │  FileSystemProjectManager        │
    │  VsProjectManager              │    │  AzdoLogger (--azdo mode)        │
    │  WPF UI / DTE / VS SDK         │    │  ServiceContext (wiring)         │
    │  SqlMigrationOrchestrator      │    │  PipelineOutput (ADO vars)       │
    │  PowerShellScriptRunner        │    │  InitCommand (init subcommand)   │
    │  AssemblyResolver              │    │                                  │
    └───────────────┬────────────────┘    └───────────────┬──────────────────┘
                    │                                     │
                    └────────────────┬────────────────────┘
                                     │
             ┌───────────────────────────────────────────────┐
             │          WTW.Diffusion.Core                   │
             │          .NET Standard 2.0                    │
             │                                               │
             │  Abstractions/                                │
             │    ILogger, IConfigurationProvider,           │
             │    IProjectManager                            │
             │                                               │
             │  Services/Database/                           │
             │    SqlMigrationRepository (+ interface)       │
             │    ChangeDetectionService                     │
             │    AzureSqlAuthenticationProvider             │
             │                                               │
             │  Services/Migration/                          │
             │    MigrationScriptManager (+ interface)       │
             │    MigrationScriptGenerator                   │
             │    IMigrationExecutor                         │
             │    PowerShellOutputParser, PowerShellResult   │
             │                                               │
             │  Services/                                    │
             │    ShadowDatabaseManager                      │
             │                                               │
             │  Orchestration/                               │
             │    IOrchestratorStep, OrchestratorContext     │
             │                                               │
             │  Models, Config, Helpers, Constants           │
             └───────────────────────────────────────────────┘

### Abstractions (defined in Core, implemented per host)

| Interface | VSIX implementation | CLI implementation |
|---|---|---|
| `ILogger` | `VsLogger` -> `OutputWindowLogger` | `ConsoleLogger` / `AzdoLogger` |
| `IConfigurationProvider` | `VsConfigurationProvider` -> `DataFoundryOptions` | (reads CLI args directly) |
| `IProjectManager` | `VsProjectManager` -> `ProjectFileManager` (DTE) | `FileSystemProjectManager` (XML) |

---

## NuGet Package Versions

### `WTW.Diffusion.Core` (netstandard2.0)

| Package | Version |
|---|---|
| `Microsoft.Data.SqlClient` | 5.1.5 |
| `Newtonsoft.Json` | 13.0.3 |
| `Microsoft.Identity.Client` | 4.83.3 |
| `Azure.Identity` | 1.21.0 |
| `Azure.Core` | 1.53.0 |

### `data-foundry` VSIX (net472)

| Package | Version | Notes |
|---|---|---|
| `Microsoft.VisualStudio.SDK` | 17.0.32112.339 | VS SDK meta-package |
| `Microsoft.VSSDK.BuildTools` | 17.14.2120 | VSIX build targets |
| `Newtonsoft.Json` | 13.0.3 | |
| `System.Management.Automation` | **5.1.1 -- pinned** | v6+ targets netcoreapp; v7+ targets net8; neither works with net472 |
| `System.Threading.Tasks.Extensions` | **4.6.3** | Delivers assembly 4.2.4.0 -- must match Core's transitive dep; see assembly binding section |
| `System.Buffers` | 4.5.1 | |
| `System.Runtime.CompilerServices.Unsafe` | 6.0.0 | |
| `Microsoft.Identity.Client` | 4.83.3 | |

### `WTW.Diffusion.Cli` (net8)

| Package | Version |
|---|---|
| `System.CommandLine` | 2.0.0-beta4.22272.1 |
| `Newtonsoft.Json` | 13.0.3 |

### `WTW.Diffusion.Tests` (net8)

| Package | Version |
|---|---|
| `xunit` | 2.9.3 |
| `xunit.runner.visualstudio` | 2.8.2 |
| `Microsoft.NET.Test.Sdk` | 17.12.0 |
| `FluentAssertions` | 6.12.2 |
| `Moq` | 4.20.72 |

### `WTW.Diffusion.Vsix.Tests` (net472)

| Package | Version |
|---|---|
| `xunit` | 2.9.3 |
| `xunit.runner.visualstudio` | 2.8.2 |
| `Microsoft.NET.Test.Sdk` | 17.12.0 |
| `FluentAssertions` | 6.12.2 |

---

## Key Files -- WTW.Diffusion.Core

    WTW.Diffusion.Core/
    +-- Constants.cs
    |       All string/regex constants (FileExtensions, Folders, Tables,
    |       Azure, RegularExpressions, Parameters, Actions)
    +-- Abstractions/
    |   +-- ILogger.cs              Log / LogError / LogWarning / LogDebug
    |   +-- IConfigurationProvider  LocalDatabaseConnection, ShadowDatabaseConnection,
    |   |                           SqlProject, MigrationsFolder, UsePowerShellScript,
    |   |                           PowerShellScriptPath, TrackedTables,
    |   |                           MinSqlServerVersion (null=auto from DSP, 0=skip)
    |   +-- IProjectManager.cs      AddFileToProject / GetProjectPath / GetRelativeFolderPath
    +-- Config/
    |   +-- TableListConfig.cs      { List<string> Tables } -- JSON key: "tables"
    |   +-- DataFoundryConfig.cs    static LoadTableList() -- reads tablelist.json from
    |                               PathHelper.GetExtensionInstallDirectory()/Config/
    +-- Helpers/
    |   +-- PathHelper.cs           SanitizePathComponent, IsValidPath, SafeCombine,
    |                               GetSafeDirectoryName, GetExtensionInstallDirectory,
    |                               EnsureDirectoryExists
    |   +-- SqlProjectDspReader.cs  ReadDsp / ParseMinimumMajorVersion / ValidateAndWarn
    |                               Reads <DSP> from .sqlproj; maps numeric suffix to SQL
    |                               Server major version; advisory warning if below minimum
    +-- Models/
    |   +-- ActivityEntry.cs        INotifyPropertyChanged; ActivityType / ActivityStatus enums
    |   +-- DatabaseChange.cs       Basic change metadata
    |   +-- MigrationAction.cs      enum: Revert | Migrate | Cancel
    |   +-- MigrationInfo.cs        { Guid Id, string Content, FileName, FullPath }
    |   +-- ShadowDatabaseCacheInfo { Hash, MigrationCount, LastUpdated, DatabaseName }
    |   |                           Serialised to shadow-cache.json
    |   +-- TableChangeSummary.cs   { Table, int Inserts, Updates, Deletes }
    |                               HasChanges => any > 0
    +-- Orchestration/
    |   +-- IOrchestratorStep.cs    IOrchestratorStep.ExecuteAsync(OrchestratorContext)
    |                               OrchestratorContext carries shared state bag
    +-- Services/
        +-- ShadowDatabaseManager.cs
        |       EnsureUpToDate(ct) -- two-level cache (memory + disk)
        |       Recreate(ct) -- drop, create, apply all migrations in order
        |       GetMigrationsHash() -- SHA256 of filename+LastWriteUtc ticks
        |       static InvalidateCache()
        +-- Database/
        |   +-- ISqlMigrationRepository.cs      Testability interface for all DB operations
        |   |                                   GetServerMajorVersion(string database)
        |   +-- SqlMigrationRepository.cs       Microsoft.Data.SqlClient implementation
        |   |       ExecuteScriptAndLog()        Atomic: script + __MigrationLog insert in one tx
        |   |       ExecuteSqlScript()           Splits on GO, runs batches
        |   |       DatabaseExists / CreateDatabaseIfMissing
        |   |       DropAndRecreateDatabase      SET SINGLE_USER + DROP + CREATE
        |   |       GetPrimaryKeyColumns / GetNonPrimaryColumns
        |   |       GetColumnMetadata / GetTableData
        |   +-- ChangeDetectionService.cs
        |   |       GetTableDiffCounts()         Single query for Inserts/Updates/Deletes counts
        |   |       GetChangesSummary(tables,ct) Per-table loop; ct checked each iteration
        |   |       RevertChanges()              DELETE rows not in shadow, INSERT missing rows
        |   |       ValidateIdentifier()         OWASP SQL injection guard (called on all identifiers)
        |   |       QuoteIdentifier()            [bracket] escaping with ] doubling
        |   +-- AzureSqlAuthenticationProvider  DefaultAzureCredential -> access token
        |                                       for *.database.windows.net targets
        +-- Migration/
            +-- IMigrationExecutor.cs           Strategy: C# path or PowerShell path
            +-- IMigrationScriptManager.cs      GetMigrationInfoFromFile / GetPendingMigrations
            |                                   GetRelativeFilename / ExecuteMigrationScript
            +-- MigrationScriptManager.cs
            |       GetPendingMigrations()      Reads *.sql, parses GUID header,
            |                                   cross-references __MigrationLog
            |       ExecuteMigrationScript(skipExecution)
            |                                   skipExecution=true -> logs GUID only (post-generate)
            |       GetFileChecksum()           SHA256 of file bytes
            +-- MigrationScriptGenerator.cs
            |       GenerateMigrationScript()   Builds INSERT/UPDATE/DELETE for each changed table
            |                                   Picks deepest subfolder of outputDir
            |                                   FormatSqlLiteral() handles NULL, DateTime, bool,
            |                                   byte[], Guid types
            +-- PowerShellOutputParser.cs       ParseChanges / ParsePendingMigrations /
            |                                   ParseGeneratedScriptPath -- parses PS stdout
            +-- PowerShellResult.cs             { bool Success, List<string> Output, Errors }

---

## Key Files -- data-foundry (VSIX)

    data-foundry/
    +-- data_foundryPackage.cs
    |       AsyncPackage; [ProvideAutoLoad] on NoSolution + SolutionExists
    |       InitializeAsync: calls AssemblyResolver.Initialize() FIRST,
    |       stores static Instance, registers ShowDataFoundryToolWindowCommand
    +-- AssemblyResolver.cs
    |       AppDomain.CurrentDomain.AssemblyResolve hook
    |       Must be registered BEFORE any other code in InitializeAsync
    |       Handles: Azure.Core, Azure.Identity, Microsoft.Identity.Client,
    |         System.Memory, System.Text.Json, System.Threading.Tasks.Extensions,
    |         System.Runtime.CompilerServices.Unsafe, System.Buffers
    |       Fallback order: extension dir -> VS IDE dir ->
    |         PublicAssemblies -> PrivateAssemblies
    +-- ShowDataFoundryToolWindowCommand.cs   Menu command to open the tool window
    +-- DataFoundryToolWindow.cs              ToolWindowPane hosting TabbedContentControl
    +-- DataFoundryToolWindowControl.xaml(.cs) Root WPF control
    +-- app.config                            Assembly binding redirects (see assembly binding section)
    +-- Options/
    |   +-- DataFoundryOptions.cs
    |   |       DialogPage; Tools -> Options -> WTW Diffusion -> General
    |   |       Properties: LocalDatabaseConnection, ShadowDatabaseConnection,
    |   |         SqlProject, MigrationsFolder, UsePowerShellScript,
    |   |         AutoRefresh, ShowNotifications, VerboseLogging,
    |   |         TrackedTables (get/set reads/writes tablelist.json)
    |   |       Build Options: SqlMinServerVersion (empty=auto from DSP, "0"=skip)
    |   |       OnApply: fires SettingsChangedService + DatabaseSyncStatusService
    |   +-- ConnectionStringEditor.cs    UITypeEditor for SQL connection string picker
    |   +-- SqlProjectListConverter.cs   TypeConverter populating SQL project dropdown
    |   +-- MigrationsFolderListConverter TypeConverter populating migrations folder dropdown
    +-- Services/
    |   +-- SqlMigrationOrchestrator.cs
    |   |       Main coordinator; two constructors:
    |   |         (DataFoundryOptions, DTE) -- from VSIX package
    |   |         (string db, server, migrationsPath, ...) -- programmatic
    |   |       Dual mode: UsePowerShellScript -> PowerShellMigrationExecutor,
    |   |         else C# services
    |   |       Methods: ExecuteTargetMigrations(ct), DetectAndHandleChanges(ct),
    |   |         Execute(ct), GenerateMigrationScriptWithName, RevertChanges(ct)
    |   |       Private: ValidateServerVersion() -- calls SqlProjectDspReader.ValidateAndWarn
    |   |         at top of ExecuteTargetMigrations()
    |   +-- SqlMigrationOrchestratorFactory.cs
    |   |       Create(package), CreateFromGlobalPackage()
    |   |       CreateAdapters() -> (ILogger, IConfigProvider, IProjectManager)
    |   +-- CSharpMigrationExecutor.cs         IMigrationExecutor -> SqlMigrationOrchestrator
    |   +-- PowerShellMigrationExecutor.cs     IMigrationExecutor -> PowerShellScriptRunner
    |   |                                      All methods sync-over-async via JoinableTaskFactory.Run
    |   +-- PowerShellScriptRunner.cs          Runs SqlMetadataAutomation.ps1 via Runspace API
    |   |                                      Uses System.Management.Automation 5.1.1
    |   +-- PowerShellOrchestratorWrapper.cs   Thin shim around PowerShellMigrationExecutor
    |   +-- OutputWindowLogger.cs
    |   |       Static; creates "WTW Diffusion" output pane (GUID-keyed)
    |   |       Log / LogError / LogWarning / Clear / Show
    |   +-- ProjectFileManager.cs              DTE-based; AddFileToProject / GetProjectPath
    |   +-- ProjectIntegrationService.cs       Wraps ProjectFileManager; called post-script-gen
    |   +-- ActivityHistoryService.cs          Singleton; StartActivity / UpdateActivity
    |   +-- ChangeDetectionResultsService.cs   Singleton event bus; UpdateResults / ClearResults
    |   +-- DatabaseSyncStatusService.cs       Singleton; NotifySyncStatusChanged event
    |   +-- SettingsChangedService.cs          Singleton; NotifySettingsChanged event
    |   +-- GlobalProcessingStateService.cs
    |   |       Singleton; TryStartProcessing / CompleteProcessing / CancelOperation
    |   |       Exposes CancellationToken -- passed by Views into all orchestrator calls
    |   +-- TrackedTablesManager.cs            GetTrackedTables / SaveTrackedTables / Add / Remove
    |   +-- ServicesHelper.cs                  ParseConnectionString -> (Database, Server)
    |   |                                      Uses System.Data.SqlClient.SqlConnectionStringBuilder
    |   +-- ConsoleLogger.cs                   Fallback ILogger (VS-internal use only)
    |   +-- Adapters/
    |       +-- VsLogger.cs                    ILogger -> OutputWindowLogger; UI-thread marshal
    |       |                                  Respects VerboseLogging flag for LogDebug
    |       +-- VsConfigurationProvider.cs     IConfigurationProvider -> DataFoundryOptions
    |       |                                  TrackedTables reads tablelist.json via DataFoundryConfig
    |       |                                  MinSqlServerVersion parses SqlMinServerVersion string
    |       +-- VsProjectManager.cs            IProjectManager -> ProjectFileManager (DTE)
    +-- Converters/
    |   +-- NullToVisibilityConverter.cs       WPF value converter
    +-- Views/Controls/
        +-- TabbedContentControl.xaml(.cs)     Root tab strip: Overview / Changes / Deployment / Settings
        +-- OverviewTabControl.xaml(.cs)
        |       Sync status, pending migrations, deploy button
        |       Passes GlobalProcessingStateService.CancellationToken to all orchestrator calls
        |       Catches OperationCanceledException separately; logs clean cancellation message
        +-- ChangesTabControl.xaml(.cs)
        |       Refresh / Generate Script / Revert buttons + diff grid
        |       Passes GlobalProcessingStateService.CancellationToken to all orchestrator calls
        |       Catches OperationCanceledException separately; logs clean cancellation message
        +-- DeploymentTabControl.xaml(.cs)     Deploy migrations to target
        +-- SettingsTabControl.xaml(.cs)       Settings summary / link to Options
        +-- NoSqlProjectMessageControl.xaml    Shown when no .sqlproj found in solution

---

## Key Files -- WTW.Diffusion.Cli

    WTW.Diffusion.Cli/
    +-- Program.cs
    |       18-line top-level entry point; registers 4 subcommands on RootCommand;
    |       no root handler (shows help when called with no subcommand)
    |       Exit codes: 0=ok, 1=cancelled, 2=unhandled error, 3=.sqlproj not found
    +-- Adapters/
    |   +-- ConsoleLogger.cs        ILogger -> stdout; colour-coded (White/Red/Yellow/DarkGray)
    |   +-- AzdoLogger.cs           ILogger; wraps ConsoleLogger; emits ##[error], ##[warning],
    |   |                           ##vso[task.setvariable], ##vso[task.uploadsummary]
    |   |                           Helpers: SetVariable / SetOutputVariable / UploadSummary / AddBuildTag
    |   +-- FileSystemProjectManager.cs
    |                               IProjectManager; adds <Build Include="..."/> to .sqlproj via XDocument
    |                               GetProjectPath: searches solutionRoot for *.sqlproj by name
    +-- Commands/
    |   +-- InitCommand.cs          `wtw-diffusion init [--output dir]`
    |   |                           Scaffolds config.json with { "TrackedTables": ["dbo.MyTable"] }
    |   +-- DeployCommand.cs        `wtw-diffusion deploy`
    |   |                           Applies all pending migrations; no shadow interaction
    |   +-- DetectChangesCommand.cs `wtw-diffusion detect-changes`
    |   |                           Recreates shadow, diffs tracked tables, applies chosen action
    |   |                           (Revert | Migrate | Cancel); prompts interactively if --action omitted
    |   +-- GenerateScriptCommand.cs `wtw-diffusion generate-script`
    |                               Same as detect-changes with action=Migrate implied
    +-- Infrastructure/
        +-- ServiceContext.cs       ServiceContext.Build(server, db, migrationsPath, ...)
        |                           Wires all Core services; shadow-cache.json beside executable
        +-- PipelineOutput.cs       Publish(pendingCount, changes, scriptPath, tempDir)
        |                           Sets ADO pipeline variables and uploads Markdown summary tab
        +-- CliOptions.cs           Static factory methods returning new Option<T> instances
        |                           (safe to add to multiple commands; avoids registration conflicts)
        +-- CommandHelpers.cs       Shared handler steps: BuildContext, PrepareDatabase,
        |                           CheckServerVersion, ApplyPendingMigrations,
        |                           RunChangeDetection, GenerateMigrationScript,
        |                           AddScriptToProject, PublishAzdoOutput
        +-- TrackedTablesConfig.cs  { string[] TrackedTables } -- deserialised from config.json

---

## Key Files -- Tests

    WTW.Diffusion.Tests/
    +-- Core/
    |   +-- SqlIdentifierTests.cs             ValidateIdentifier: injection, length, brackets, schemas
    |   +-- PathHelperTests.cs                SafeCombine, IsValidPath, GetSafeDirectoryName
    |   +-- MigrationScriptGeneratorTests.cs  INSERT/UPDATE/DELETE generation; SQL literal escaping
    |   |                                     Uses Mock<ISqlMigrationRepository>; IDisposable temp dir
    |   +-- PowerShellOutputParserTests.cs    ParseChanges, ParsePendingMigrations, ParseGeneratedScriptPath
    |   +-- SqlProjectDspReaderTests.cs       ReadDsp (temp XML file), ParseMinimumMajorVersion
    |                                         (all DSP strings, Azure, unknown), ValidateAndWarn
    |                                         (override=null/0/positive, DSP present/absent/Azure)
    +-- Cli/
        +-- PipelineOutputTests.cs            BuildMarkdown: no-change, pending, change table, script section

    WTW.Diffusion.Vsix.Tests/  (VSIX service files compiled as linked files -- no VS SDK needed)
    +-- Services/
        +-- GlobalProcessingStateServiceTests.cs
        +-- ChangeDetectionResultsServiceTests.cs
        +-- EventServiceTests.cs
        +-- ServicesHelperTests.cs

---

## Key Workflows

### 1. Apply pending migrations

`MigrationScriptManager.GetPendingMigrations(database)`:
1. `GetExecutedMigrationIds()` -- reads GUIDs from `dbo.__MigrationLog`
2. Scans `*.sql` recursively in migrations folder, ordered by path
3. Parses each file for `-- <Migration ID="{guid}" />`
4. Returns files whose GUID is not in the executed set

`MigrationScriptManager.ExecuteMigrationScript(database, info, skipExecution)`:
- `skipExecution=false` (normal): calls `repository.ExecuteScriptAndLog()` -- atomic transaction
- `skipExecution=true` (post-generate): calls `repository.LogMigrationExecution()` only

### 2. Detect data changes

1. `ShadowDatabaseManager.EnsureUpToDate(ct)` -- checks in-memory hash, then disk cache, recreates if stale
2. `ChangeDetectionService.GetChangesSummary(target, shadow, tables, ct)` -- one SQL query per table
3. Returns `List<TableChangeSummary>` with per-table insert/update/delete counts

### 3. Generate a migration script

`MigrationScriptGenerator.GenerateMigrationScript(target, shadow, tables, outputDir, scriptName)`:
1. Picks latest subfolder of `outputDir`
2. Writes header: `-- <Migration ID="{newGuid}" />`
3. Per table: generates DELETEs (rows in shadow not in target), then INSERTs, then UPDATEs
4. `MigrationScriptManager.ExecuteMigrationScript(..., skipExecution: true)` logs the GUID so the script will not re-run
5. `ProjectIntegrationService.AddScriptToProject()` adds it to `.sqlproj`

### 4. Revert changes

`ChangeDetectionService.RevertChanges(target, shadow, tables)`:
- Tables with PK: DELETE rows not in shadow, INSERT rows from shadow missing in target
- Tables without PK: TRUNCATE + INSERT SELECT * FROM shadow

---

## Database Conventions

### Migration log table -- `dbo.__MigrationLog`

Every migration script must contain this comment on line 1:

```sql
-- <Migration ID="{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}" />
GO
```

`MigrationLogTableDefinition.sql` is in `Config/` and is deployed into the VSIX package and beside
the CLI executable. Actual column names:

| Column | Type | Description |
|---|---|---|
| `migration_id` | `uniqueidentifier` | GUID from the script header |
| `script_filename` | `nvarchar(500)` | Relative path from migrations root |
| `complete_dt` | `datetime2` | `SYSDATETIME()` at execution time |
| `script_checksum` | `nvarchar(64)` | SHA-256 hex of file bytes |
| `applied_by` | `nvarchar` | `SYSTEM_USER` at execution time |
| `deployed` | `bit` | Always `1` |
| `version`, `package_version`, `release_version` | `nvarchar` | Nullable; reserved |

**Atomicity rule**: `SqlMigrationRepository.ExecuteScriptAndLog()` wraps both the script execution
and the `INSERT INTO __MigrationLog` in a single transaction. **Never** call `ExecuteSqlScript` +
`LogMigrationExecution` as two separate calls.

Scripts whose GUID already exists in `__MigrationLog` are never re-executed.

### Change detection hashing

Per-row comparison uses a single SQL statement that computes three counts in one round trip:

```sql
SELECT
  (rows in target WHERE NOT EXISTS in shadow)  AS Inserts,
  (rows in both WHERE HASHBYTES('SHA2_256', concat_non_pk_cols) differs) AS Updates,
  (rows in shadow WHERE NOT EXISTS in target) AS Deletes
```

Null handling: `ISNULL(CONVERT(NVARCHAR(MAX), col), '#NULL#')` before hashing.
`CHECKSUM()` is explicitly **not** used -- it has known collision cases.

### Shadow database caching

Two-level cache to avoid unnecessary rebuilds:

1. **In-memory** -- `static string _lastShadowMigrationHash` on `ShadowDatabaseManager`
2. **On-disk** -- `shadow-cache.json` beside `tablelist.json`; contains hash, migrationCount, lastUpdated, databaseName

Cache key: SHA256 of all `.sql` filenames + `LastWriteTimeUtc.Ticks`, sorted by path, joined with `|`.

`ShadowDatabaseManager.InvalidateCache()` clears the in-memory hash, forcing re-evaluation on next call.

### SQL injection prevention

`ChangeDetectionService.ValidateIdentifier()` is called on every database name and table name:
- Database names: rejects `'`, `"`, `;`, `--`, `/*`, `*/`, `xp_`, `sp_`
- Table/column names: must match `^[a-zA-Z_][a-zA-Z0-9_]*$` (after stripping brackets)
- Max length: 128 characters

`QuoteIdentifier()` wraps identifiers in `[brackets]` and doubles any internal `]` characters.

### Script generation output location

`MigrationScriptGenerator.GenerateMigrationScript()` picks the **lexicographically last subfolder**
of `outputDir` (e.g., `Migrations/2025/`). If no subfolders exist, writes directly to `outputDir`.

---

## VSIX Assembly Binding -- Critical Details

The VSIX runs on .NET Framework 4.7.2 inside Visual Studio. VSSDK build tools exclude several
assemblies from the VSIX package as "platform assemblies" even when Core requires them.

### app.config binding redirects

```xml
<dependentAssembly>
  <assemblyIdentity name="Microsoft.Identity.Client" publicKeyToken="0a613f4dd989e8ae" />
  <bindingRedirect oldVersion="0.0.0.0-4.83.3.0" newVersion="4.83.3.0" />
</dependentAssembly>
<dependentAssembly>
  <assemblyIdentity name="Azure.Core" publicKeyToken="92742159e12e44c8" />
  <bindingRedirect oldVersion="0.0.0.0-1.53.0.0" newVersion="1.53.0.0" />
</dependentAssembly>
<dependentAssembly>
  <assemblyIdentity name="System.Memory" publicKeyToken="cc7b13ffcd2ddd51" />
  <bindingRedirect oldVersion="0.0.0.0-4.0.5.0" newVersion="4.0.5.0" />
</dependentAssembly>
<dependentAssembly>
  <assemblyIdentity name="System.Text.Json" publicKeyToken="cc7b13ffcd2ddd51" />
  <bindingRedirect oldVersion="0.0.0.0-8.0.0.6" newVersion="8.0.0.6" />
</dependentAssembly>
<dependentAssembly>
  <assemblyIdentity name="System.Runtime.CompilerServices.Unsafe" publicKeyToken="b03f5f7f11d50a3a" />
  <bindingRedirect oldVersion="0.0.0.0-6.0.0.0" newVersion="6.0.0.0" />
</dependentAssembly>
<dependentAssembly>
  <assemblyIdentity name="System.Threading.Tasks.Extensions" publicKeyToken="cc7b13ffcd2ddd51" />
  <bindingRedirect oldVersion="0.0.0.0-4.2.4.0" newVersion="4.2.4.0" />
</dependentAssembly>
```

### AssemblyResolver

`AssemblyResolver.Initialize()` must be called **first** in `data_foundryPackage.InitializeAsync()`,
before any other code runs. It hooks `AppDomain.CurrentDomain.AssemblyResolve` and searches for the
assembly in this fallback order:

1. Extension install directory (normal installed VSIX case)
2. `AppDomain.CurrentDomain.BaseDirectory` (VS `Common7\IDE\`)
3. `Common7\IDE\PublicAssemblies\`
4. `Common7\IDE\PrivateAssemblies\`

### Forcing System.Threading.Tasks.Extensions into the VSIX package

VSSDK treats this as a platform assembly and excludes it. The csproj forces inclusion:

```xml
<Content Include="$(NuGetPackageRoot)system.threading.tasks.extensions\4.6.3\lib\net462\System.Threading.Tasks.Extensions.dll"
         Condition="Exists('...')">
  <Link>System.Threading.Tasks.Extensions.dll</Link>
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
  <IncludeInVSIX>true</IncludeInVSIX>
</Content>
```

The NuGet reference must be `Version="4.6.3"` -- this package delivers assembly version 4.2.4.0,
which is what Core's transitive dependencies compile against.

---

## Coding Conventions

### Namespace conflicts in VSIX
- **Never** use `using static WTW.Diffusion.Core.Constants` in VSIX files -- conflicts with `EnvDTE.Constants`
- Use alias: `using CoreConstants = WTW.Diffusion.Core.Constants;`
- Reference as: `CoreConstants.Folders.Config`, `CoreConstants.Azure.AzureSqlDomain`, etc.

### SqlClient choice
- Core: `using Microsoft.Data.SqlClient` (active development, Azure AD support)
- VSIX: `using System.Data.SqlClient` (BCL; legacy compat with net472)

### .NET Standard 2.0 constraints (Core)
- `DataTable.AsEnumerable()` is **not available** -- use `Rows.Cast<DataRow>()` instead
- `DataTableExtensions` (`System.Data.DataSetExtensions`) is not available

### COM interop (VSIX)
- `ProjectItems` is a COM interface -- `null` is the correct "not found" sentinel
- Suppress SonarQube S1168 with `[SuppressMessage]` on method + `// NOSONAR` on line

### CancellationToken threading
- `GlobalProcessingStateService.CancellationToken` is passed from Views into all orchestrator calls
- `SqlMigrationOrchestrator` public methods accept `CancellationToken ct = default`
- Core services call `ct.ThrowIfCancellationRequested()` at the start of each loop iteration
- `OperationCanceledException` is caught separately from `Exception` in View code-behinds

### Cognitive complexity
All methods must have cognitive complexity <= 12. Break complex logic into private helpers.

---

## CI/CD Integration (`wtw-diffusion` CLI)

The CLI replaces `SqlMetadataAutomation.ps1`. All options have dual aliases:
`--kebab-case` (new standard) and `--PascalCase` (legacy PS compatibility).

### Subcommands

#### `wtw-diffusion deploy`

| Option | Required | Description |
|---|---|---|
| `--target-database` | Yes | Database name |
| `--target-server` | Yes | SQL Server hostname or instance |
| `--migrations-path` | Yes | Path to migration scripts folder |
| `--confirm-target-migration` | | Prompt before executing pending migrations |
| `--migration-log-schema` | | Path to `MigrationLogTableDefinition.sql`; defaults to beside executable |
| `--sql-project` | | `.sqlproj` name for DSP version check |
| `--solution-root` | | Directory to search for `.sqlproj`; defaults to parent of `--migrations-path` |
| `--min-sql-version` | | Override minimum SQL Server version (0=skip, null=auto from DSP) |
| `--azdo` | | ADO mode: emit `##[error]`, `##vso[...]` commands and upload summary tab |

#### `wtw-diffusion detect-changes`

All `deploy` options plus:

| Option | Required | Description |
|---|---|---|
| `--shadow-database` | | Shadow DB name; defaults to `{TargetDatabase}_Shadow` |
| `--config-path` | | Path to `config.json`; defaults to `./config.json` |
| `--action` | | `Revert` \| `Migrate` \| `Cancel`; prompts interactively if omitted |
| `--output-migration-dir` | | Where to write generated scripts; defaults to `--migrations-path` |
| `--script-name` | | Script filename (no extension); prompts if omitted |

#### `wtw-diffusion generate-script`

Same options as `detect-changes` minus `--action` (Migrate is implied).

#### `wtw-diffusion init [--output dir]`

Creates a starter `config.json`.

### Azure DevOps pipeline variables set by `--azdo`

| Variable | Example |
|---|---|
| `wtw.hasChanges` | `true` |
| `wtw.pendingMigrations` | `3` |
| `wtw.changedTables` | `dbo.Users,dbo.Orders` |
| `wtw.totalChanges` | `14` |
| `wtw.generatedScript` | `C:\agent\...\001_Seed.sql` |

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Cancelled by user (`OperationCanceledException`) |
| `2` | Unhandled error |
| `3` | `.sqlproj` not found (`--sql-project` specified but project could not be located) |

---

## Configuration

### `tablelist.json` -- tracked tables (VSIX)

Located at `{extension install dir}/Config/tablelist.json`. JSON key is `"tables"` (lowercase).
Editable via **Tools -> Options -> WTW Diffusion -> General -> Tracked Tables** (comma-separated UI).

```json
{ "tables": ["LookupCodes", "Roles", "Permissions"] }
```

### CLI `config.json`

JSON key is `"TrackedTables"` (note: different casing from VSIX). Generated by `wtw-diffusion init`.

```json
{ "TrackedTables": ["dbo.LookupCodes", "dbo.Roles"] }
```

---

## Build Notes

- **VSIX must be built through Visual Studio** -- `dotnet build` / MSBuild direct invocation fails (WPF/WinFx targets require VS toolchain)
- **Core and CLI** can be built independently with `dotnet build`
- **Tests**: `dotnet test WTW.Diffusion.Tests` -- 80 tests, no VS required
- **Vsix.Tests**: compiles VSIX service files as linked files to avoid VS SDK dependency; run via VS Test Explorer
- After deleting VSIX source files, delete `obj\` before rebuilding to clear CS2001 ghost errors
- `InternalsVisibleTo("WTW.Diffusion.Tests")` is set in both Core and CLI csproj files

---

## Testing Strategy

| Project | Framework | What it tests |
|---|---|---|
| `WTW.Diffusion.Tests` | xUnit / net8 | SQL identifier validation, PathHelper, script generation SQL output (mocked `ISqlMigrationRepository`), PowerShell output parsing, CLI pipeline markdown |
| `WTW.Diffusion.Vsix.Tests` | xUnit / net472 | `GlobalProcessingStateService`, `ChangeDetectionResultsService`, event services, `ServicesHelper.ParseConnectionString` |

VS-coupled code (DTE, Output Window, WPF) is covered by manual end-to-end testing in the experimental VS instance.

---

## Technology Choices

| Decision | Choice | Reason |
|---|---|---|
| Core target framework | .NET Standard 2.0 | Consumed by both net472 VSIX and net8 CLI |
| VSIX target framework | .NET Framework 4.7.2 | Required by VS 2022/2026 extension model |
| CLI target framework | .NET 8 | Cross-platform publish, self-contained binary |
| SQL client (Core) | `Microsoft.Data.SqlClient` 5.1.5 | Active development, Azure AD auth support |
| SQL client (VSIX) | `System.Data.SqlClient` | Legacy compat with net472 VS toolchain |
| JSON serialisation | `Newtonsoft.Json` 13.0.3 | Consistent across all projects |
| CLI argument parsing | `System.CommandLine` 2.0.0-beta4 | Double-dash conventions, built-in `--help` |
| Change detection | `HASHBYTES('SHA2_256', ...)` | Collision-resistant; `CHECKSUM()` is not |
| Test framework | xUnit + FluentAssertions + Moq | Standard modern .NET test stack |
| Azure auth | `Azure.Identity` `DefaultAzureCredential` | Works for local dev, MI, service principal |
| Orchestration future | `IOrchestratorStep` / `OrchestratorContext` | Foundation for future visual pipeline designer |

---

## Known Constraints

- **VSIX must be built through Visual Studio** -- `dotnet build` cannot resolve VS SDK targets
- **`System.Management.Automation` is pinned at 5.1.1** -- v6+ targets netcoreapp, v7+ targets net8; neither is compatible with net472. Upgrading requires migrating the VSIX to `net8.0-windows` (SDK-style, VS 2022 17.9+)
- **`System.Threading.Tasks.Extensions` must be explicitly included in the VSIX** -- VSSDK build tools exclude it as a "platform assembly"; see the forced `<Content IncludeInVSIX="true">` item in `data-foundry.csproj`
- **`EnvDTE.Constants` namespace conflict** -- never use `using static WTW.Diffusion.Core.Constants` in VSIX files; use alias `using CoreConstants = WTW.Diffusion.Core.Constants`
- **`DataTable.AsEnumerable()`** is not available in .NET Standard 2.0 -- use `Rows.Cast<DataRow>()` instead
- **COM interop (`ProjectItems`)** has no empty constructor -- `null` is the correct "not found" sentinel

---

## Remaining Work

### Low Priority

| Item | Detail |
|---|---|
| **GitHub Actions workflow template** | A reusable workflow YAML for `.github/workflows/`, parallel to the ADO pipeline example. |
| **Publish `wtw-diffusion` to an internal NuGet feed** | Enables `dotnet tool install wtw-diffusion` in pipelines without a file copy step. |
| **Migrate VSIX to `net8.0-windows` SDK-style** | Unblocks PowerShell 7, async improvements, and modern SDK features. Requires VS 2022 17.9+. Should be bundled with any effort to upgrade `System.Management.Automation`. |
| **Rename VSIX project to `WTW.Diffusion.Extension`** | Low impact; requires solution restructure and updating all project/namespace references. |
| **UI/UX polish** | Spacing, alignment, grid column auto-sizing across all tab controls. |

### Completed

| Item | Status |
|---|---|
| **Wire `IConfigurationProvider` / `IProjectManager` into `SqlMigrationOrchestrator`** | ✅ Done — `SqlMigrationOrchestrator` primary constructor accepts `(ILogger, IConfigurationProvider, IProjectManager, ...)`. `SqlMigrationOrchestratorFactory` creates and passes the three VS adapters. No `DataFoundryOptions` dependency in Core. |
| **`RevertChanges` efficiency** | ✅ Done — `SqlMigrationOrchestrator.RevertChanges(List<string> tableNames, CancellationToken ct)` calls `ChangeDetectionService.RevertChanges()` directly, skipping shadow sync. `ChangesTabControl` passes the already-known `changesWithDiffs` table names. |
| **`CancellationToken` in CLI** | ✅ Done — `context.GetCancellationToken()` is extracted at handler start and passed to `ctx.ShadowManager.Recreate(ct)`. Ctrl+C is handled automatically by `System.CommandLine`. |
| **`FileSystemProjectManager` in CLI Migrate path** | ✅ Done — `ServiceContext.Build()` wires `FileSystemProjectManager` when `solutionRoot` is provided; CLI Migrate path calls `ctx.ProjectManager.AddFileToProject(...)` after generating a script. |
| **Minimum SQL Server version check from `.sqlproj` DSP** | ✅ Done — `SqlProjectDspReader.ValidateAndWarn` (Core); `ISqlMigrationRepository.GetServerMajorVersion`; `IConfigurationProvider.MinSqlServerVersion`; `DataFoundryOptions.SqlMinServerVersion`; `SqlMigrationOrchestrator.ValidateServerVersion()`; CLI `--min-sql-version` on all three subcommands; 14 tests in `SqlProjectDspReaderTests`. |
| **CLI subcommands (`deploy` / `detect-changes` / `generate-script`)** | ✅ Done — `Program.cs` stripped to 18 lines; logic extracted to `Commands/` + `Infrastructure/CommandHelpers.cs` + `Infrastructure/CliOptions.cs`; 80 tests passing. |
# WTW Diffusion — Copilot Instructions

## Project Overview
A Visual Studio extension (VSIX) that detects data changes between a target SQL Server database and a shadow database, generates migration scripts, and automates deployment. Designed for use with SQL Server Data Projects (`.sqlproj`).

## Solution Structure

| Project | Framework | Purpose |
|---|---|---|
| `data-foundry` (VSIX) | .NET 4.7.2 | VS extension shell — all VS SDK, DTE, WPF UI |
| `WTW.Diffusion.Core` | .NET Standard 2.0 | Platform-independent business logic |
| `WTW.Diffusion.Cli` | .NET 8 _(planned)_ | CLI tool for Azure DevOps / GitHub Actions pipelines |

## Architecture Rules

### What goes in Core (`WTW.Diffusion.Core`)
- SQL database operations, migration script management, change detection logic
- Data models, configuration models, Azure authentication
- `ShadowDatabaseManager`, `ChangeDetectionService`, `MigrationScriptGenerator`, `MigrationScriptManager`, `SqlMigrationRepository`
- Path utilities (`PathHelper`)
- Orchestration abstractions (`IOrchestratorStep`, `OrchestratorContext`)

### What stays in VSIX (`data-foundry`)
- Anything using `EnvDTE` / DTE
- Anything using `Microsoft.VisualStudio.Shell`
- WPF/XAML controls and code-behind
- `OutputWindowLogger`, `ProjectFileManager`, `ProjectIntegrationService`
- `PowerShellScriptRunner` (uses `System.Management.Automation` — Windows-only SDK)
- `ActivityHistoryService`, `SettingsChangedService`, `DatabaseSyncStatusService`, `GlobalProcessingStateService`
- Adapter implementations: `VsLogger`, `VsConfigurationProvider`, `VsProjectManager`

## Critical Coding Conventions

### Namespace conflicts in VSIX
- **Never** use `static using WTW.Diffusion.Core.Constants` in VSIX files — conflicts with `EnvDTE.Constants`
- Always add `using` directives for Core namespaces (e.g. `using WTW.Diffusion.Core.Services.Database;`) and use simple type names — do **not** fully qualify inline
- For `Constants` specifically, use a type alias to avoid the `EnvDTE.Constants` conflict: `using CoreConstants = WTW.Diffusion.Core.Constants;` then reference `CoreConstants.Folders.X`, `CoreConstants.Azure.X`
- Always fully qualify: `WTW.Diffusion.Core.Config.DataFoundryConfig`, `WTW.Diffusion.Core.Config.TableListConfig` only when a `using` would cause ambiguity

### SqlClient
- Use `System.Data.SqlClient` in the VSIX orchestrator (legacy compat)
- Use `Microsoft.Data.SqlClient` in `WTW.Diffusion.Core`

### .NET Standard 2.0 constraints (Core)
- `DataTable.AsEnumerable()` is **not available** — use `Rows.Cast<DataRow>()` instead
- `DataTableExtensions` (`System.Data.DataSetExtensions`) is not available

### COM interop (VSIX)
- `ProjectItems` is a COM interface — there is no empty instantiation
- `null` is the correct sentinel for "not found" — suppress SonarQube S1168 with `[SuppressMessage]` on the method and `// NOSONAR` on the line

### Migration execution
- Script execution and `__MigrationLog` writes must always be atomic — use `SqlMigrationRepository.ExecuteScriptAndLog()` (wraps both in a single transaction)
- Never call `ExecuteSqlScript` + `LogMigrationExecution` as two separate calls

### Change detection
- Use `HASHBYTES('SHA2_256', ...)` with `ISNULL(CONVERT(NVARCHAR(MAX), col), '#NULL#')` for update detection — never `CHECKSUM()` (collision-prone)

## Key Files

| File | Purpose |
|---|---|
| `Services/SqlMigrationOrchestrator.cs` | Thin coordinator — delegates to managers below |
| `WTW.Diffusion.Core/Services/ShadowDatabaseManager.cs` | Shadow DB lifecycle: hash cache, recreate, validate |
| `WTW.Diffusion.Core/Services/Database/ChangeDetectionService.cs` | Detects and reverts data changes |
| `WTW.Diffusion.Core/Services/Migration/MigrationScriptGenerator.cs` | Generates migration SQL from diffs |
| `WTW.Diffusion.Core/Services/Migration/MigrationScriptManager.cs` | Pending migration discovery and execution |
| `WTW.Diffusion.Core/Services/Database/SqlMigrationRepository.cs` | All direct SQL operations |
| `WTW.Diffusion.Core/Orchestration/IOrchestratorStep.cs` | Step interface + context for future designer |
| `Services/ProjectIntegrationService.cs` | Adds generated scripts to the VS SQL project |
| `Services/Adapters/VsLogger.cs` | `ILogger` ? Output Window |
| `Services/Adapters/VsConfigurationProvider.cs` | `IConfigurationProvider` ? `DataFoundryOptions` |
| `Services/Adapters/VsProjectManager.cs` | `IProjectManager` ? DTE `ProjectFileManager` |
| `AssemblyResolver.cs` | Runtime fallback for Azure.Identity, Azure.Core, MSAL, System.Memory |

## Known Patterns

### Shadow database caching
SHA256 hash of all `.sql` filenames + `LastWriteTimeUtc` ticks. Two-level cache: in-memory (`static` field) + disk (`shadow-cache.json`). Managed entirely by `ShadowDatabaseManager`.

### Dual execution mode
Operations run via C# (`CSharpMigrationExecutor`) or PowerShell (`PowerShellMigrationExecutor`). Toggled by `DataFoundryOptions.UsePowerShellScript`. Both implement `IMigrationExecutor`.

### Migration log table
`dbo.__MigrationLog` — GUID embedded in each script as `-- <Migration ID="{guid}" />`. Applied migrations are never re-executed.

### Tracked tables config
`Config/tablelist.json` in the extension install directory. Editable via **Tools > Options > WTW Diffusion > Tracked Tables**.

## Pending Work (Priority Order)
1. Create `WTW.Diffusion.Cli` project (`net8.0`) — `ConsoleLogger`, `FileConfigurationProvider`, `FileSystemProjectManager`, commands: `detect-changes`, `generate-script`, `deploy`
2. Wire `ILogger`, `IConfigurationProvider`, `IProjectManager` through `SqlMigrationOrchestrator` (replace `Action<string>` logger pattern)
3. UI/UX polish, `CancellationToken` support, error handling improvements

## Build Notes
- VSIX must be built via Visual Studio — `dotnet build` / `MSBuild.exe` direct invocation fails (WPF/WinFx targets require VS toolchain)
- `WTW.Diffusion.Core` can be built with `dotnet build` independently
- If CS2001 ghost errors appear for deleted VSIX files, delete `obj\` and rebuild

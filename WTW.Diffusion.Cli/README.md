# WTW Diffusion CLI

Cross-platform command-line tool for running WTW Diffusion SQL migration and data change detection in CI/CD pipelines. Drop-in replacement for `SqlMetadataAutomation.ps1`.

Built on [WTW.Diffusion.Core](../WTW.Diffusion.Core) — the same business logic used by the Visual Studio extension.

---

## Requirements

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server or Azure SQL reachable from the agent
- `MigrationLogTableDefinition.sql` placed beside the executable (or passed via `--migration-log-schema`)

---

## Installation

### Build from source

```bash
cd WTW.Diffusion.Cli
dotnet publish -c Release -r win-x64 --self-contained
```

The output binary is `wtw-diffusion.exe` (or `wtw-diffusion` on Linux/macOS).

### As a .NET tool (once published to a feed)

```bash
dotnet tool install --global WTW.Diffusion.Cli
```

---

## Quick Start

### 1. Create a config file

```bash
wtw-diffusion init
```

This creates a `config.json` in the current directory:

```json
{
  "TrackedTables": [
    "dbo.MyTable"
  ]
}
```

Edit `TrackedTables` to list every table you want change detection to watch.

### 2. Apply pending migrations

```bash
wtw-diffusion \
  --target-server ".\SQLEXPRESS" \
  --target-database "MyAppDb" \
  --migrations-path "C:\Repos\MyApp\Database\Migrations"
```

### 3. Detect data changes and generate a migration script

```bash
wtw-diffusion \
  --target-server ".\SQLEXPRESS" \
  --target-database "MyAppDb" \
  --migrations-path "C:\Repos\MyApp\Database\Migrations" \
  --detect-changes \
  --action Migrate \
  --script-name "001_US123456_SeedLookupTables" \
  --config-path ".\config.json"
```

---

## All Options

| Option | Alias | Required | Default | Description |
|---|---|---|---|---|
| `--target-database` | `--TargetDatabase` | [x] | — | Target SQL Server database name |
| `--target-server` | `--TargetServer` | [x] | — | SQL Server hostname or instance (e.g. `.\SQLEXPRESS`, `myserver.database.windows.net`) |
| `--migrations-path` | `--MigrationsPath` | [x] | — | Root folder containing `.sql` migration scripts (searched recursively) |
| `--detect-changes` | `--DetectChanges` | | `false` | Recreate shadow database and compare tracked tables against target |
| `--action` | `--Action` | | interactive prompt | Action after change detection: `Revert` \| `Migrate` \| `Cancel` |
| `--config-path` | `--ConfigPath` | | `./config.json` | Path to `config.json` containing the `TrackedTables` array |
| `--script-name` | `--ScriptName` | | interactive prompt | Name for the generated script when `--action Migrate` (no extension) |
| `--output-migration-dir` | `--OutputMigrationDir` | | `--migrations-path` | Root folder for the generated migration script |
| `--shadow-database` | `--ShadowDatabase` | | `{TargetDatabase}_Shadow` | Name of the shadow database |
| `--migration-log-schema` | `--MigrationLogSchema` | | beside executable | Path to `MigrationLogTableDefinition.sql` |
| `--confirm-target-migration` | `--ConfirmTargetMigration` | | `false` | Prompt for confirmation before applying pending migrations |
| `--azdo` | | | `false` | Emit Azure DevOps logging commands and publish a pipeline summary tab |

---

## Subcommands

### `init`

Scaffolds a starter `config.json` in the current (or specified) directory.

```bash
wtw-diffusion init
wtw-diffusion init --output "C:\Repos\MyApp"
```

---

## Azure SQL Authentication

If `--target-server` contains `database.windows.net`, the CLI automatically acquires an access token using [DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential).

The following credential sources are tried in order:

1. Environment variables (`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`)
2. Managed Identity (works on Azure-hosted agents automatically)
3. Azure CLI (`az login`)
4. Visual Studio / VS Code credentials

No password or connection string required for Azure SQL.

---

## Azure DevOps Integration

Pass `--azdo` to activate pipeline mode. This:

1. **Emits `##[error]` and `##[warning]`** logging commands so ADO surfaces them in the step annotation and log viewer
2. **Sets pipeline variables** readable by all subsequent steps in the job
3. **Uploads a Markdown summary tab** to the pipeline run page

### Pipeline variables set

| Variable | Type | Example |
|---|---|---|
| `wtw.hasChanges` | `true` \| `false` | `true` |
| `wtw.pendingMigrations` | integer | `3` |
| `wtw.changedTables` | comma-separated | `dbo.Users,dbo.Orders` |
| `wtw.totalChanges` | integer | `14` |
| `wtw.generatedScript` | file path | `C:\agent\...\001_Seed.sql` |

### Example `azure-pipelines.yml`

```yaml
steps:
  - task: UseDotNet@2
    inputs:
      version: '8.x'

  - script: |
      wtw-diffusion \
        --target-server "$(SqlServer)" \
        --target-database "$(SqlDatabase)" \
        --migrations-path "$(Build.SourcesDirectory)/Database/Migrations" \
        --config-path "$(Build.SourcesDirectory)/config.json" \
        --detect-changes \
        --action Migrate \
        --script-name "Auto_$(Build.BuildId)" \
        --azdo
    name: wtw
    displayName: "WTW Diffusion — detect & migrate"

  # Only runs when the previous step detected data changes
  - script: |
      echo "Generated script: $(wtw.generatedScript)"
      echo "Tables changed:   $(wtw.changedTables)"
    displayName: "Review migration output"
    condition: eq(variables['wtw.hasChanges'], 'true')

  # Gate: fail the pipeline if unexpected changes exist and no action was taken
  - script: exit 1
    displayName: "Fail on unhandled changes"
    condition: |
      and(
        eq(variables['wtw.hasChanges'], 'true'),
        eq(variables['wtw.generatedScript'], '')
      )
```

### Azure Managed Identity setup

On a self-hosted or Microsoft-hosted agent, grant your pipeline's managed identity (or service principal) the following SQL Server permissions:

```sql
-- Run once against target and shadow databases
CREATE USER [<managed-identity-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [<managed-identity-name>];
```

---

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Operation cancelled by user (`--confirm-target-migration` rejected) |
| `2` | Unhandled error (see stderr / `##[error]` in ADO) |

---

## config.json Reference

```json
{
  "TrackedTables": [
    "dbo.Users",
    "dbo.Roles",
    "dbo.LookupCodes"
  ]
}
```

Table names must match the `dbo`-qualified name exactly as stored in SQL Server. Use `SELECT TABLE_SCHEMA + '.' + TABLE_NAME FROM INFORMATION_SCHEMA.TABLES` to verify.

---

## How It Works

```
wtw-diffusion
    │
    ├─ Apply pending migrations to target database
    │      Scans --migrations-path for *.sql files not yet in __MigrationLog
    │      Executes each script atomically (script + log write in one transaction)
    │
    └─ (if --detect-changes)
           │
           ├─ Recreate shadow database
           │      Drops & recreates {TargetDatabase}_Shadow
           │      Applies all migrations from scratch
           │
           ├─ Compare tracked tables
           │      Uses HASHBYTES SHA-256 row comparison
           │      Reports inserts, updates, deletes per table
           │
           └─ Act on differences
                  Revert  → MERGE shadow → target (restore baseline)
                  Migrate → Generate INSERT/UPDATE/DELETE script
                  Cancel  → Report only, no changes applied
```

---

## Related

- [WTW Diffusion Visual Studio Extension](../README.md) — the VSIX counterpart
- [WTW.Diffusion.Core](../WTW.Diffusion.Core) — shared business logic library
- [Azure DevOps Logging Commands](https://learn.microsoft.com/azure/devops/pipelines/scripts/logging-commands)

<#
.SYNOPSIS
    Orchestrates SQL Server database migration execution, shadow database creation, and data change detection / synchronization.
.DESCRIPTION
    Implements the workflow described in copilot-instructions.md:
      1. Execute pending migration scripts against a target SQL Server database (required)
      2. Optionally execute pending migration scripts against a shadow database (fresh create)
      3. Optionally detect data changes between target and shadow for tracked tables and either revert or generate a migration script.
.PARAMETER TargetDatabase
    Name of the target database (e.g. AppDb).
.PARAMETER TargetServer
    SQL Server instance or hostname (e.g. .\SQLEXPRESS or myserver.database.windows.net).
.PARAMETER MigrationsPath
    Path containing migration script files (searches recursively). Excludes 001_ConsolidatedMigrations_Schema.sql from pending logic.
.PARAMETER ConfirmTargetMigration
    Switch; if provided, user confirmation is required before executing pending migrations on target.
.PARAMETER DetectChanges
    Switch; if provided, compares tracked tables between target and shadow for inserts / updates / deletes.
.PARAMETER Action
    Optional choice after DetectChanges. Values: Revert | Migrate. If not supplied and differences found, user will be prompted.
.PARAMETER ConfigPath
    Path to config.json containing TrackedTables array.
.PARAMETER OutputMigrationDir
    Optional root path for generated migration script when Action=Migrate. Defaults to MigrationsPath.
.PARAMETER ScriptName
    Optional name for the generated migration script when Action=Migrate. If not provided, user will be prompted.
.EXAMPLE
    # Integrated security (local dev)
    ./SqlMetadataAutomation.ps1 -TargetDatabase AppDb -TargetServer .\SQLEXPRESS -MigrationsPath ./Migrations -ConfirmTargetMigration -DetectChanges
    # SQL Auth (Azure)
    ./SqlMetadataAutomation.ps1 -TargetDatabase AppDb -TargetServer myserver.database.windows.net -MigrationsPath ./Migrations
.NOTES
    Requires SqlServer PowerShell module (Invoke-Sqlcmd). Uses SHA256 file checksum for logging. Adds clear section markers for future extension.
#>
[CmdletBinding()] param(
    [Parameter(Mandatory = $true)] [string]$TargetDatabase,
    [Parameter(Mandatory = $true)] [string]$TargetServer,
    [Parameter(Mandatory = $true)] [string]$MigrationsPath,
    [Parameter(Mandatory = $false)] [switch]$ConfirmTargetMigration,
    [Parameter(Mandatory = $false)] [switch]$DetectChanges,
    [Parameter(Mandatory = $false)] [ValidateSet('Revert', 'Migrate', 'Cancel')] [string]$Action,
    [Parameter(Mandatory = $false)] [string]$ConfigPath = (Join-Path $PSScriptRoot 'config.json'),
    [Parameter(Mandatory = $false)] [string]$OutputMigrationDir,
    [Parameter(Mandatory = $false)] [string]$ScriptName
)

# --- Retrieve SQL Access Token  -----------------------------------------------
function Get-AzureSqlAccessToken {
    # Return Azure SQL access token only if target server appears to be an Azure SQL instance
    if ($TargetServer -and $TargetServer -like '*database.windows.net*') {
        try {
            return Get-AzAccessToken -ResourceUrl "https://database.windows.net/"
        }
        catch {
            Write-Warning "Unable to acquire Azure SQL access token for server '$TargetServer': $($_.Exception.Message)"
        }
    }
    return $null
}

$AzureSqlAccessToken = Get-AzureSqlAccessToken

# --- Environment / Pre-flight -------------------------------------------------
if (-not (Get-Module -ListAvailable -Name SqlServer)) { Throw 'SqlServer PowerShell module is required. Install-Module SqlServer' }
if (-not (Test-Path $MigrationsPath)) { Throw "MigrationsPath not found: $MigrationsPath" }
if (-not (Test-Path $ConfigPath)) { Throw "ConfigPath not found: $ConfigPath" }
if (-not $OutputMigrationDir) { $OutputMigrationDir = $MigrationsPath }

$Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$ShadowDatabase = "${TargetDatabase}_Shadow"

# --- Utility Functions --------------------------------------------------------
function Invoke-DbCommand {
    param([string]$Database, [string]$Query, [string]$ErrorAction = 'Stop')
    $params = @{
        Database       = $Database;
        ServerInstance = $TargetServer;
        Query          = $Query;
        ErrorAction    = $ErrorAction
    }
    
    if ($AzureSqlAccessToken) {
        $params.AccessToken = $AzureSqlAccessToken.Token
    }
    
    $result = Invoke-Sqlcmd @params

    # Normalize SQL NULLs so they remain $null instead of becoming empty strings downstream.
    if ($null -eq $result) { return $result }
    $result | ForEach-Object {
        if ($_ -is [System.Data.DataRow]) {
            $row = $_
            $map = @{}
            foreach ($col in $row.Table.Columns) {
                $name = $col.ColumnName
                $val = $row.$name
                if ($val -is [DBNull]) { $val = $null }
                $map[$name] = $val
            }
            [pscustomobject]$map
        }
        else {
            $_.PSObject.Properties.ForEach({
                    if ($_.Value -is [DBNull]) { $_.Value = $null }
                })
            $_
        }
    }
}

function Test-DatabaseExists {
    param([string]$Database)
    $q = "SELECT CASE WHEN DB_ID('$Database') IS NULL THEN 0 ELSE 1 END AS exists_flag;"
    $row = Invoke-DbCommand -Database "master" -Query $q -ErrorAction Stop
    return ([bool]($row.exists_flag -eq 1))
}

function New-DatabaseIfMissing {
    param([string]$Database, [string]$MigrationsPath)
    if (Test-DatabaseExists -Database $Database) { return }
    Write-Host "Creating database $Database"
    try {
        Invoke-DbCommand -Database "master" -Query "CREATE DATABASE [$Database];" -ErrorAction Stop
    }
    catch {
        Write-Error "Database creation failed for '$Database': $($_.Exception.Message)"
        throw "Aborting: unable to create database '$Database'."
    }
    # Verify creation
    if (-not (Test-DatabaseExists -Database $Database)) {
        Write-Error "Database '$Database' not found after CREATE DATABASE command. Aborting script."
        throw "Aborting: database '$Database' does not exist after attempted creation."
    }
    Write-Host "Database $Database created successfully."
}

function Ensure-MigrationLogTable {
    param([string]$Database)
    $row = Invoke-DbCommand -Database $Database -Query "SELECT CASE WHEN OBJECT_ID('[dbo].[__MigrationLog]') IS NULL THEN 0 ELSE 1 END AS exists_flag;"
    if (-not $row.exists_flag) {
        Write-Host "Creating __MigrationLog table..."
        $ddlPath = Join-Path $PSScriptRoot 'MigrationLogTableDefinition.sql'
        $ddl = Get-Content $ddlPath -Raw
        Invoke-DbCommand -Database $Database -Query $ddl
    }
}

function Get-ExecutedMigrationIds {
    param([string]$Database)
    $rows = Invoke-DbCommand -Database $Database -Query 'SELECT migration_id FROM dbo.__MigrationLog'
    return $rows.migration_id
}

function Get-MigrationInfoFromFile {
    param([string]$Path)
    # Preserve full path so scripts in subfolders can be executed & checksummed correctly.
    $text = Get-Content $Path -Raw
    $guid = [regex]::Match($text, '--\s*<Migration\s+ID="(?<id>[0-9a-fA-F-]{36})"\s*/>')
    if (-not $guid.Success) { return $null }
    return [PSCustomObject]@{ Id = $guid.Groups['id'].Value; Content = $text; FileName = (Split-Path $Path -Leaf); FullPath = $Path }
}

function Get-PendingMigrations {
    param([string]$MigrationsPath, [string]$Database)
    $executed = Get-ExecutedMigrationIds -Database $Database
    $files = Get-ChildItem -Path $MigrationsPath -Recurse -File -Include '*.sql' | Sort-Object -Property FullName
    $pending = @()
    foreach ($f in $files) {
        $info = Get-MigrationInfoFromFile -Path $f.FullName
        if ($info -and ($executed -notcontains $info.Id)) { $pending += $info }
    }
    return $pending
}

function Get-FileChecksumSha256 {
    param([string]$Path)
    (Get-FileHash $Path -Algorithm SHA256).Hash
}

function Log-MigrationExecution {
    param([string]$Database, [string]$MigrationId, [string]$FileName, [string]$Checksum)
    $sql = @"
INSERT INTO dbo.__MigrationLog (migration_id, script_checksum, script_filename, complete_dt, applied_by, deployed, version, package_version, release_version)
VALUES ('$MigrationId', '$Checksum', '$FileName', SYSDATETIME(), SYSTEM_USER, 1, NULL, NULL, NULL);
"@
    Invoke-DbCommand -Database $Database -Query $sql
}

function Get-MigrationRelativeFilename {
    param([string]$Root, [string]$FullPath)
    try {
        $rootResolved = (Resolve-Path $Root).Path
        $fullResolved = (Resolve-Path $FullPath).Path
    }
    catch { return (Split-Path $FullPath -Leaf) }
    $rootLeaf = Split-Path $rootResolved -Leaf
    if ($fullResolved.StartsWith($rootResolved, [System.StringComparison]::OrdinalIgnoreCase)) {
        $relative = $fullResolved.Substring($rootResolved.Length).TrimStart('\', '/')
        if ([string]::IsNullOrEmpty($relative)) { return "$rootLeaf\" + (Split-Path $fullResolved -Leaf) }
        return "$rootLeaf\$relative"
    }
    return (Split-Path $fullResolved -Leaf)
}

function Invoke-MigrationScript {
    param([string]$Database, [object]$MigrationInfo, [string]$FullPath, [string]$MigrationsRoot, [bool]$SkipExecution)
    $displayName = Get-MigrationRelativeFilename -Root $MigrationsRoot -FullPath $FullPath
    if (!$SkipExecution) {
        Write-Host "Executing migration $($MigrationInfo.Id) ($displayName) on $Database"
        Invoke-DbCommand -Database $Database -Query $MigrationInfo.Content
    }
    $checksum = Get-FileChecksumSha256 -Path $FullPath
    Log-MigrationExecution -Database $Database -MigrationId $MigrationInfo.Id -FileName $displayName -Checksum $checksum
}

function Drop-And-RecreateDatabase {
    param([string]$Database, [string]$MigrationsPath)
    if (Test-DatabaseExists -Database $Database) {
        Write-Host "Dropping existing database $Database"
        Invoke-DbCommand -Database "master" -Query "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database];"
    }
    New-DatabaseIfMissing -Database $Database -MigrationsPath $MigrationsPath
}

# --- Diff / Change Detection --------------------------------------------------
function Get-PrimaryKeyColumns {
    param([string]$Database, [string]$Table)
    $q = @"
SELECT c.name FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
JOIN sys.columns c ON ic.object_id=c.object_id AND ic.column_id=c.column_id
WHERE i.is_primary_key=1 AND OBJECT_NAME(i.object_id)='$Table' ORDER BY ic.key_ordinal;
"@
    (Invoke-DbCommand -Database $Database -Query $q).name
}

function Get-NonPrimaryColumns {
    param([string]$Database, [string]$Table, [string[]]$Pk)
    $q = "SELECT name FROM sys.columns WHERE OBJECT_ID='$([int](Invoke-DbCommand -Database $Database -Query "SELECT OBJECT_ID('$Table') oid").oid)'"
    $cols = (Invoke-DbCommand -Database $Database -Query $q).name
    $cols | Where-Object { $Pk -notcontains $_ }
}

function Get-TableDiffCounts {
    param([string]$TargetDatabase, [string]$ShadowDatabase, [string]$Table)
    # Approximate updates by comparing hashes of non-PK cols for matching PKs
    $pk = Get-PrimaryKeyColumns -Database $TargetDatabase -Table $Table
    if (-not $pk -or $pk.Count -eq 0) { $updateCount = 0 } else {
        $join = ($pk | ForEach-Object { "p.[$_]=s.[$_]" }) -join ' AND '
        $nonPk = Get-NonPrimaryColumns -Database $TargetDatabase -Table $Table -Pk $pk
        if ($nonPk.Count -gt 0) {
            $hashCols = ($nonPk | ForEach-Object { "ISNULL(CONVERT(nvarchar(max),p.[$_]),'#NULL#')" }) -join ", '|' ,"
            $hashColsShadow = ($nonPk | ForEach-Object { "ISNULL(CONVERT(nvarchar(max),s.[$_]),'#NULL#')" }) -join ", '|' ,"
            if ($nonPk.Count -gt 1) {
                $hashCols = "CONCAT($hashCols)"
                $hashColsShadow = "CONCAT($hashColsShadow)"
            }
            $updateSql = @"
            SELECT COUNT(*) AS c FROM [$TargetDatabase].dbo.[$Table] p
JOIN [$ShadowDatabase].dbo.[$Table] s ON $join
WHERE HASHBYTES('SHA2_256', $hashCols) <> HASHBYTES('SHA2_256', $hashColsShadow);
"@
            $updateCount = (Invoke-DbCommand -Database $TargetDatabase -Query $updateSql).c
        }
        else { $updateCount = 0 }
    }
    # Counts using EXCEPT set differences
    $insertCount = (Invoke-DbCommand -Database $TargetDatabase -Query "SELECT COUNT(*) AS c FROM (SELECT * FROM [$TargetDatabase].dbo.[$Table] EXCEPT SELECT * FROM [$ShadowDatabase].dbo.[$Table]) x").c - $updateCount
    $deleteCount = (Invoke-DbCommand -Database $TargetDatabase -Query "SELECT COUNT(*) AS c FROM (SELECT * FROM [$ShadowDatabase].dbo.[$Table] EXCEPT SELECT * FROM [$TargetDatabase].dbo.[$Table]) x").c - $updateCount
    [PSCustomObject]@{ Table = $Table; Inserts = $insertCount; Updates = $updateCount; Deletes = $deleteCount }
}

function Get-ChangesSummary {
    param([string]$TargetDatabase, [string]$ShadowDatabase, [string[]]$Tables)
    $results = @()
    foreach ($t in $Tables) { $results += Get-TableDiffCounts -TargetDatabase $TargetDatabase -ShadowDatabase $ShadowDatabase -Table $t }
    return $results
}

# --- Revert & Migration Script Generation (Simplified) -----------------------
function Invoke-RevertChanges {
    param([string]$TargetDatabase, [string]$ShadowDatabase, [string[]]$Tables)
    foreach ($t in $Tables) {
        Write-Host "Reverting table [dbo].[$t]"
        # Delete rows not in shadow
        $deleteSql = "DELETE p FROM [$TargetDatabase].dbo.[$t] p LEFT JOIN [$ShadowDatabase].dbo.[$t] s ON 1=1 WHERE NOT EXISTS (SELECT 1 FROM [$ShadowDatabase].dbo.[$t] WHERE "
        $pk = Get-PrimaryKeyColumns -Database $TargetDatabase -Table $t
        if ($pk.Count -gt 0) {
            $pkJoinExists = ($pk | ForEach-Object { "[$ShadowDatabase].dbo.[$t].[${_}] = [$TargetDatabase].dbo.[$t].[${_}]" }) -join ' AND '
            $deleteSql = "DELETE FROM [$TargetDatabase].dbo.[$t] WHERE NOT EXISTS (SELECT 1 FROM [$ShadowDatabase].dbo.[$t] WHERE $pkJoinExists)"
            Invoke-DbCommand -Database $TargetDatabase -Query $deleteSql -NoThrow
            # Upsert pattern: MERGE for inserts/updates
            $sourceCols = (Invoke-DbCommand -Database $ShadowDatabase -Query "SELECT name FROM [$ShadowDatabase].sys.columns WHERE [object_id] = (SELECT [object_id] FROM [$ShadowDatabase].sys.[tables] WHERE [name] = '$t')") | Select-Object -ExpandProperty name
            $colList = ($sourceCols | ForEach-Object { "[$_]" }) -join ', '
            $onClause = ($pk | ForEach-Object { "TARGET.[$_]=SOURCE.[$_]" }) -join ' AND '
            $updateSet = ($sourceCols | Where-Object { $pk -notcontains $_ } | ForEach-Object { "TARGET.[$_] = SOURCE.[$_]" }) -join ', '
            $merge = @"
MERGE [$TargetDatabase].dbo.[$t] AS TARGET USING [$ShadowDatabase].dbo.[$t] AS SOURCE ON ($onClause)
WHEN MATCHED THEN UPDATE SET $updateSet
WHEN NOT MATCHED BY TARGET THEN INSERT ($colList) VALUES ($colList);
"@
            Invoke-DbCommand -Database $TargetDatabase -Query $merge -NoThrow
        }
        else {
            # Fallback: truncate and copy if no PK
            Invoke-DbCommand -Database $TargetDatabase -Query "TRUNCATE TABLE [$TargetDatabase].dbo.[$t]" -NoThrow
            Invoke-DbCommand -Database $TargetDatabase -Query "INSERT INTO [$TargetDatabase].dbo.[$t] SELECT * FROM [$ShadowDatabase].dbo.[$t]" -NoThrow
        }
    }
    Write-Host 'Revert complete.'
}

function New-MigrationScriptFromDiff {
    param([string]$TargetDatabase, [string]$ShadowDatabase, [string[]]$Tables, [string]$OutputDir, [string]$ScriptName = $null)
    function Format-SqlLiteral {
        param($Value)
        if ($null -eq $Value) { return 'NULL' }
        if ($Value -is [DateTime]) { return "'" + $Value.ToString('yyyy-MM-dd HH:mm:ss.fff') + "'" }
        if ($Value -is [bool]) { return ($(if ($Value) { 1 }else { 0 })) }
        if ($Value -is [byte[]]) { return '0x' + ($Value | ForEach-Object { $_.ToString('X2') } -join '') }
        if ($Value -is [string]) {
            $escaped = $Value.Replace("'", "''")
            return "'${escaped}'"
        }
        if ($Value -is [guid]) { return "'$($Value.ToString())'" }
        if ($Value -is [System.IFormattable]) { return $Value.ToString() }
        return "'" + ($Value.ToString().Replace("'", "''")) + "'"
    }

    if ([string]::IsNullOrEmpty($ScriptName)) {
        $ScriptName = Read-Host "Enter your migration script name without the extension (ex: 001_US123456_1_Description)"
    }
    else {
        Write-Host "Using provided script name: $ScriptName"
    }
    $guid = [guid]::NewGuid().ToString()
    $fileName = "$ScriptName.sql"
    $targetFolder = $OutputDir
    $subfolders = Get-ChildItem -Path $OutputDir -Directory | Sort-Object Name
    if ($subfolders.Count -gt 0) { $targetFolder = $subfolders[-1].FullName }
    if (-not (Test-Path $targetFolder)) { New-Item -ItemType Directory -Path $targetFolder | Out-Null }
    $fullPath = Join-Path $targetFolder $fileName

    $sb = New-Object System.Text.StringBuilder
    $null = $sb.AppendLine("-- <Migration ID=`"$guid`" />")
    $null = $sb.AppendLine("GO")
    $null = $sb.AppendLine("SET DATEFORMAT YMD;")
    $null = $sb.AppendLine("GO")

    foreach ($t in $Tables) {
        $pk = Get-PrimaryKeyColumns -Database $TargetDatabase -Table $t
        $colsQuery = @"
SELECT c.name AS ColumnName, t.name AS TypeName
FROM [$TargetDatabase].sys.columns c
JOIN [$TargetDatabase].sys.types t ON c.user_type_id=t.user_type_id
WHERE c.object_id = OBJECT_ID('[$TargetDatabase].dbo.[$t]')
ORDER BY c.column_id;
"@
        $colMeta = Invoke-DbCommand -Database $TargetDatabase -Query $colsQuery
        $allCols = $colMeta.ColumnName

        if (-not $pk -or $pk.Count -eq 0) {
            # Requirement: assume tracked tables have PK; if missing, skip with warning.
            Write-Warning "Skipping table '$t' in migration generation: no primary key detected."
            $null = $sb.AppendLine("-- Skipped table $t\: no primary key detected at generation time.")
            continue
        }

        # Build temp tables for diffing via EXCEPT and hash detection (in generation only)
        $targetRows = Invoke-DbCommand -Database $TargetDatabase -Query "SELECT * FROM [$TargetDatabase].dbo.[$t]"
        $shadowRows = Invoke-DbCommand -Database $ShadowDatabase -Query "SELECT * FROM [$ShadowDatabase].dbo.[$t]"

        # Create dictionaries keyed by PK composite string
        $targetDict = @{}
        foreach ($r in $targetRows) {
            $key = ($pk | ForEach-Object { ($r.$_) -as [string] }) -join '||'
            $targetDict[$key] = $r
        }
        $shadowDict = @{}
        foreach ($r in $shadowRows) {
            $key = ($pk | ForEach-Object { ($r.$_) -as [string] }) -join '||'
            $shadowDict[$key] = $r
        }

        $nonPk = $allCols | Where-Object { $pk -notcontains $_ }

        # Deletes (present in shadow, absent in target)
        $deleteKeys = $shadowDict.Keys | Where-Object { -not $targetDict.ContainsKey($_) }
        if ($deleteKeys.Count -gt 0) {
            $null = $sb.AppendLine("PRINT (N'Delete $($deleteKeys.Count) row(s) from [dbo].[$t]');")
            foreach ($k in $deleteKeys) {
                $pkPredicates = @()
                $row = $shadowDict[$k]
                foreach ($c in $pk) { $pkPredicates += "[${c}] = " + (Format-SqlLiteral $row.$c) }
                $where = $pkPredicates -join ' AND '
                $null = $sb.AppendLine("DELETE FROM [dbo].[$t] WHERE $where;")
            }
            $null = $sb.AppendLine("GO")
        }

        # Inserts (present in target, absent in shadow)
        $insertKeys = $targetDict.Keys | Where-Object { -not $shadowDict.ContainsKey($_) }
        if ($insertKeys.Count -gt 0) {
            $null = $sb.AppendLine("PRINT (N'Add $($insertKeys.Count) row(s) to [dbo].[$t]');")
            foreach ($k in $insertKeys) {
                $row = $targetDict[$k]
                $values = @()
                foreach ($c in $allCols) { $values += (Format-SqlLiteral $row.$c) }
                $colList = ($allCols | ForEach-Object { "[${_}]" }) -join ', '
                $valList = $values -join ', '
                $null = $sb.AppendLine("INSERT INTO [dbo].[$t] ($colList) VALUES ($valList);")
            }
            $null = $sb.AppendLine("GO")
        }

        # Updates (PK exists in both, non-PK differs)
        $updateKeys = $targetDict.Keys | Where-Object { $shadowDict.ContainsKey($_) }
        $updateStatements = @()
        foreach ($k in $updateKeys) {
            $pRow = $targetDict[$k]
            $sRow = $shadowDict[$k]
            $setClauses = @()
            foreach ($c in $nonPk) {
                $pVal = $pRow.$c
                $sVal = $sRow.$c
                $equal = ($pVal -eq $sVal)
                if (-not $equal) {
                    $setClauses += "[${c}] = " + (Format-SqlLiteral $pVal)
                }
            }
            if ($setClauses.Count -gt 0) {
                $pkPredicates = @()
                foreach ($c in $pk) { $pkPredicates += "[${c}] = " + (Format-SqlLiteral $pRow.$c) }
                $where = $pkPredicates -join ' AND '
                $setList = $setClauses -join ', '
                $updateStatements += ("UPDATE dbo.[$t] SET $setList WHERE $where;")
            }
        }
        if ($updateStatements -gt 0) {
            $null = $sb.AppendLine("PRINT (N'Update $($updateStatements.Count) row(s) in [dbo].[$t]');")
            $updateStatements.ForEach({ $null = $sb.AppendLine($_) } )
            $null = $sb.AppendLine("GO")
        }
    }

    Set-Content -Path $fullPath -Value $sb.ToString() -Encoding UTF8
    Write-Host "Generated migration script: $fullPath"
    
    $migrationInfo = Get-MigrationInfoFromFile -Path $fullPath
    if ($null -ne $migrationInfo) {
        Write-Host "Logging newly generated migration script to migration log"
        Invoke-MigrationScript -Database $TargetDatabase -MigrationInfo $migrationInfo -FullPath $fullPath -MigrationsRoot $MigrationsPath -SkipExecution $true
    }
    else {
        Write-Warning 'Unable to find pending migration script after migration script generation, cannot update migration log table with newly generated script'
    }
}

function Get-DetectChangesAction {
    $prompt = "`How would you like to handle these changes?"
    $promptOptions = "`n  1) Generate migration script"
    $promptOptions += "`n  2) Revert changes in $TargetDatabase"
    $promptOptions += "`n  3) Cancel/Exit"
    while ([bool]$prompt) {
        $answerAsString = Read-Host -Prompt $($prompt + $promptOptions + "`n")
        Write-Host $answerAsString
        switch ($answerAsString) {
            '1' {
                $result = 'Migrate'
                $prompt = ""
            }
            '2' {
                $result = 'Revert'
                $prompt = ""
            }
            '3' {
                $result = 'Cancel'
                $prompt = ""
            }
            Default {
                $result = ''
                $prompt = "Please enter one of the numbers presented"
            }
        }
    }
    return $result
}

# --- Target Migration Execution ---------------------------------------------
if (-not $AzureSqlAccessToken) {
    Write-Host "Ensuring target database"
    New-DatabaseIfMissing -Database $TargetDatabase -MigrationsPath $MigrationsPath
}
Write-Host "Ensuring migration log table"
Ensure-MigrationLogTable -Database $TargetDatabase
$pendingTarget = Get-PendingMigrations -MigrationsPath $MigrationsPath -Database $TargetDatabase
if ($pendingTarget.Count -eq 0) {
    Write-Host 'No pending migrations found.'
}
else {
    if ($ConfirmTargetMigration) {
        Write-Host 'Pending migrations:'
        $pendingTarget | ForEach-Object { Write-Host "  -> $($_.FileName) [$($_.Id)]" }
        $resp = Read-Host 'Execute these migrations? (y/n)'
        if ($resp -notin @('y', 'Y')) { Throw 'Target migrations aborted by user.' }
    }
    foreach ($m in $pendingTarget) { Invoke-MigrationScript -Database $TargetDatabase -MigrationInfo $m -FullPath $m.FullPath -MigrationsRoot $MigrationsPath }
    Write-Host "Migrations complete."
} 

# --- Change Detection & Action -----------------------------------------------
if ($DetectChanges) {
    if ($Config.PSObject.Properties.Name -contains 'TrackedTables') {
        
        # Create shadow database and apply migrations
        Write-Host "Recreating shadow database"
        Drop-And-RecreateDatabase -Database $ShadowDatabase -MigrationsPath $MigrationsPath
        Ensure-MigrationLogTable -Database $ShadowDatabase
        $pendingShadow = Get-PendingMigrations -MigrationsPath $MigrationsPath -Database $ShadowDatabase
        foreach ($m in $pendingShadow) { Invoke-MigrationScript -Database $ShadowDatabase -MigrationInfo $m -FullPath $m.FullPath -MigrationsRoot $MigrationsPath }
        Write-Host 'Shadow migrations complete.'
        
        # Detecting changes between target and shadow
        Write-Host 'Detecting changes between target and shadow...'
        $summary = Get-ChangesSummary -TargetDatabase $TargetDatabase -ShadowDatabase $ShadowDatabase -Tables $Config.TrackedTables
        $diffs = $summary | Where-Object { $_.Inserts -gt 0 -or $_.Updates -gt 0 -or $_.Deletes -gt 0 }
        if ($diffs.Count -eq 0) { Write-Host 'No changes detected.' } else {
            Write-Host 'Changes detected:'
            $diffs | Format-Table -AutoSize
            if (-not $Action) {
                $Action = Get-DetectChangesAction
            }
            switch ($Action) {
                'Revert' {
                    Invoke-RevertChanges -TargetDatabase $TargetDatabase -ShadowDatabase $ShadowDatabase -Tables $diffs.Table
                }
                'Migrate' {
                    New-MigrationScriptFromDiff -TargetDatabase $TargetDatabase -ShadowDatabase $ShadowDatabase -Tables $diffs.Table -OutputDir $OutputMigrationDir -ScriptName $ScriptName
                }
                'Cancel' {
                    Write-Host 'Action cancelled by user. No changes applied.'
                }
                Default {
                    Write-Warning "Unknown or no action specified. Skipping change handling."
                }
            }
        }
    }
    else {
        Write-Warning 'No TrackedTables found in config.'
    }
}

[System.Data.SqlClient.SqlConnection]::ClearAllPools()

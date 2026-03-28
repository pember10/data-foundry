# WTW Diffusion - File Migration Script
# This script moves files from the old VSIX project to the new Core library
# and updates namespaces automatically

param(
    [string]$RootPath = "D:\source\data-foundry"
)

$ErrorActionPreference = "Stop"

Write-Host "===================================" -ForegroundColor Cyan
Write-Host "WTW Diffusion File Migration Script" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

# Define file mappings (source -> destination)
$filesToMove = @{
    # Models
    "Models\ActivityEntry.cs" = "WTW.Diffusion.Core\Models\ActivityEntry.cs"
    "Models\DatabaseChange.cs" = "WTW.Diffusion.Core\Models\DatabaseChange.cs"
    "Models\MigrationAction.cs" = "WTW.Diffusion.Core\Models\MigrationAction.cs"
    "Models\MigrationInfo.cs" = "WTW.Diffusion.Core\Models\MigrationInfo.cs"
    "Models\ShadowDatabaseCacheInfo.cs" = "WTW.Diffusion.Core\Models\ShadowDatabaseCacheInfo.cs"
    "Models\TableChangeSummary.cs" = "WTW.Diffusion.Core\Models\TableChangeSummary.cs"
    
    # Config
    "Config\DataFoundryConfig.cs" = "WTW.Diffusion.Core\Config\DataFoundryConfig.cs"
    "Config\TableListConfig.cs" = "WTW.Diffusion.Core\Config\TableListConfig.cs"
    
    # Helpers
    "Helpers\PathHelper.cs" = "WTW.Diffusion.Core\Helpers\PathHelper.cs"
}

function Update-Namespace {
    param(
        [string]$FilePath,
        [string]$OldNamespace = "data_foundry",
        [string]$NewNamespace = "WTW.Diffusion.Core"
    )
    
    $content = Get-Content $FilePath -Raw
    
    # Replace namespace declaration
    $content = $content -replace "namespace $OldNamespace\.Models", "namespace $NewNamespace.Models"
    $content = $content -replace "namespace $OldNamespace\.Config", "namespace $NewNamespace.Config"
    $content = $content -replace "namespace $OldNamespace\.Helpers", "namespace $NewNamespace.Helpers"
    $content = $content -replace "namespace $OldNamespace", "namespace $NewNamespace"
    
    # Replace using statements
    $content = $content -replace "using $OldNamespace\.Models;", "using $NewNamespace.Models;"
    $content = $content -replace "using $OldNamespace\.Config;", "using $NewNamespace.Config;"
    $content = $content -replace "using $OldNamespace\.Helpers;", "using $NewNamespace.Helpers;"
    $content = $content -replace "using $OldNamespace;", "using $NewNamespace;"
    
    # Remove unnecessary using statements
    $content = $content -replace "using System\.Linq;\r?\n", ""
    $content = $content -replace "using System\.Text;\r?\n", ""
    $content = $content -replace "using System\.Threading\.Tasks;\r?\n", ""
    
    Set-Content $FilePath -Value $content -NoNewline
}

# Move and update files
foreach ($source in $filesToMove.Keys) {
    $sourcePath = Join-Path $RootPath $source
    $destPath = Join-Path $RootPath $filesToMove[$source]
    
    if (Test-Path $sourcePath) {
        Write-Host "Moving: $source -> $($filesToMove[$source])" -ForegroundColor Yellow
        
        # Ensure destination directory exists
        $destDir = Split-Path $destPath -Parent
        if (!(Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        
        # Copy file
        Copy-Item -Path $sourcePath -Destination $destPath -Force
        
        # Update namespace
        Update-Namespace -FilePath $destPath
        
        Write-Host "  ? Copied and updated namespace" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Source file not found: $source" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Migration complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Build WTW.Diffusion.Core project to verify" -ForegroundColor White
Write-Host "2. Review updated files in Core project" -ForegroundColor White
Write-Host "3. Continue with Service file migration" -ForegroundColor White

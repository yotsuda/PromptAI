<#
.SYNOPSIS
    Build and deploy PromptAI module

.DESCRIPTION
    Builds PromptAI.dll (Release, net8.0) and deploys to the PowerShell modules directory.

.EXAMPLE
    .\Build.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'PromptAI'
$stagingPath = Join-Path $PSScriptRoot 'Staging'
$outputBase = 'C:\Program Files\PowerShell\7\Modules\PromptAI'

Write-Host "=== PromptAI Build ===" -ForegroundColor Cyan

# Build
Write-Host "Building PromptAI.dll..." -ForegroundColor Yellow
$buildArgs = @('build', $projectPath, '-c', 'Release', '--no-incremental', '--source', 'https://api.nuget.org/v3/index.json')
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed!" }
Write-Host "  Build completed" -ForegroundColor Green

# Deploy
Write-Host "Deploying to $outputBase..." -ForegroundColor Yellow
if (-not (Test-Path $outputBase)) {
    New-Item -ItemType Directory -Path $outputBase -Force | Out-Null
}

$buildOutput = Join-Path $projectPath 'bin\Release\net8.0'
Copy-Item (Join-Path $buildOutput 'PromptAI.dll') -Destination $outputBase -Force
Copy-Item (Join-Path $stagingPath 'PromptAI.psd1') -Destination $outputBase -Force
Copy-Item (Join-Path $stagingPath 'PromptAI.Format.ps1xml') -Destination $outputBase -Force

Write-Host "  Copied: PromptAI.dll" -ForegroundColor Green
Write-Host "  Copied: PromptAI.psd1" -ForegroundColor Green
Write-Host "  Copied: PromptAI.Format.ps1xml" -ForegroundColor Green

Write-Host "`n=== Deploy Complete ===" -ForegroundColor Green

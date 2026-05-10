<#
.SYNOPSIS
    Build and deploy PromptAI module

.DESCRIPTION
    Builds PromptAI.dll (Release, net8.0), generates MAML help from
    docs/en-US/*.md via Microsoft.PowerShell.PlatyPS, and deploys all
    artifacts to the PowerShell modules directory.

.EXAMPLE
    .\Build.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'PromptAI'
$stagingPath = Join-Path $PSScriptRoot 'Staging'
$docsPath    = Join-Path $PSScriptRoot 'docs'
$outputBase  = 'C:\Program Files\PowerShell\7\Modules\PromptAI'

Write-Host "=== PromptAI Build ===" -ForegroundColor Cyan

# Build
Write-Host "Building PromptAI.dll..." -ForegroundColor Yellow
$buildArgs = @('build', $projectPath, '-c', 'Release', '--no-incremental', '--source', 'https://api.nuget.org/v3/index.json')
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed!" }
Write-Host "  Build completed" -ForegroundColor Green

# Generate MAML help from docs/<locale>/*.md
Write-Host "Generating MAML help..." -ForegroundColor Yellow
Import-Module Microsoft.PowerShell.PlatyPS -ErrorAction Stop
$mamlTemp = Join-Path ([System.IO.Path]::GetTempPath()) ("PromptAI-help-" + [guid]::NewGuid())
foreach ($localeDir in Get-ChildItem -Path $docsPath -Directory) {
    $mdFiles = Get-ChildItem -Path $localeDir.FullName -Filter *.md -File
    if (-not $mdFiles) { continue }
    $help = Import-MarkdownCommandHelp -Path $mdFiles.FullName
    $localeOut = Join-Path $mamlTemp $localeDir.Name
    New-Item -ItemType Directory -Path $localeOut -Force | Out-Null
    Export-MamlCommandHelp -Command $help -OutputFolder $localeOut -Force | Out-Null
    Write-Host "  Generated: $($localeDir.Name)\PromptAI.dll-Help.xml" -ForegroundColor Green
}

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

# Deploy MAML: $mamlTemp\<locale>\PromptAI\PromptAI.dll-Help.xml -> $outputBase\<locale>\PromptAI.dll-Help.xml
foreach ($localeDir in Get-ChildItem -Path $mamlTemp -Directory) {
    $mamlFile = Get-ChildItem -Path $localeDir.FullName -Filter 'PromptAI.dll-Help.xml' -Recurse -File | Select-Object -First 1
    if (-not $mamlFile) { continue }
    $destLocale = Join-Path $outputBase $localeDir.Name
    if (-not (Test-Path $destLocale)) {
        New-Item -ItemType Directory -Path $destLocale -Force | Out-Null
    }
    Copy-Item $mamlFile.FullName -Destination $destLocale -Force
    Write-Host "  Copied: $($localeDir.Name)\PromptAI.dll-Help.xml" -ForegroundColor Green
}

Remove-Item $mamlTemp -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n=== Deploy Complete ===" -ForegroundColor Green

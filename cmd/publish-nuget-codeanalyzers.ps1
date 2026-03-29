#!/usr/bin/env pwsh
# publish-nuget.ps1 — Build a release package and push to NuGet.org
#
# Usage (from anywhere):
#   .\cmd\publish-nuget.ps1          # pack + push
#   .\cmd\publish-nuget.ps1 -DryRun  # pack only, show what would be pushed
#
param(
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Resolve project root (one level up from cmd/)
$projectRoot = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $projectRoot 'AN.CodeAnalyzers.csproj'
$releaseOutputDir = Join-Path (Join-Path $projectRoot 'bin') 'Release'
$localNuGetFeedPath = 'C:\PROJECTS\LocalNuGet'

Write-Host "`n=== Packing release ===" -ForegroundColor Cyan
dotnet pack $csprojPath -c Release /p:NewRelease=true
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet pack failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

# Find the newest .nupkg (excluding prerelease packages with '-' in version)
$newestReleasePackage = Get-ChildItem (Join-Path $releaseOutputDir 'ArtificialNecessity.CodeAnalyzers.*.nupkg') |
    Where-Object { $_.Name -notmatch '\d+-\d+' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $newestReleasePackage) {
    Write-Host "ERROR: No release .nupkg found in $releaseOutputDir" -ForegroundColor Red
    exit 1
}

Write-Host "`nPackage: $($newestReleasePackage.Name)" -ForegroundColor Green

if ($DryRun) {
    Write-Host "`n[DRY RUN] Would push: $($newestReleasePackage.FullName)" -ForegroundColor Yellow
    Write-Host "[DRY RUN] To: https://api.nuget.org/v3/index.json" -ForegroundColor Yellow
    exit 0
}

Write-Host "`n=== Pushing to NuGet.org ===" -ForegroundColor Cyan
dotnet nuget push $newestReleasePackage.FullName --source https://api.nuget.org/v3/index.json --skip-duplicate
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet nuget push failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

# Also deploy to local NuGet feed
if (Test-Path $localNuGetFeedPath) {
    Copy-Item $newestReleasePackage.FullName -Destination $localNuGetFeedPath -Force
    Write-Host "Deployed to local feed: $localNuGetFeedPath" -ForegroundColor DarkGray
}

Write-Host "`n=== Done! ===" -ForegroundColor Green
Write-Host "Published: $($newestReleasePackage.Name)" -ForegroundColor Green
Write-Host "View at: https://www.nuget.org/packages/ArtificialNecessity.CodeAnalyzers/" -ForegroundColor Green
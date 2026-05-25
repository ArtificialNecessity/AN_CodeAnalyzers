#!/usr/bin/env pwsh
# publish-local.ps1 — Build and pack all packages to local NuGet feed
#
# Versioning is timestamp-based (v2) — every build gets a unique version
# automatically via AN.CodeAnalyzers.shared.Build.props. No version files to manage.
#
# Usage:
#   ./cmd/publish-local.ps1                    # Debug configuration
#   ./cmd/publish-local.ps1 -Release           # Release configuration
#
# Requires: LOCAL_NUGET_REPO environment variable set to local feed path

param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$configuration = if ($Release) { "Release" } else { "Debug" }

Write-Host "=== AN_CodeAnalyzers publish-local ($configuration) ===" -ForegroundColor Cyan

if (-not $env:LOCAL_NUGET_REPO) {
    Write-Host "ERROR: LOCAL_NUGET_REPO environment variable not set." -ForegroundColor Red
    Write-Host '$env:LOCAL_NUGET_REPO = "C:\PROJECTS\LocalNuGet"' -ForegroundColor Yellow
    exit 1
}

Write-Host "Local NuGet feed: $env:LOCAL_NUGET_REPO" -ForegroundColor Gray

# Capture timestamp before build/pack so we can identify newly deployed packages
$deployStartTime = Get-Date

# Build the solution
Write-Host "`n[1/3] Building solution..." -ForegroundColor Green
dotnet build "$repoRoot\AN_CodeAnalyzers.sln" -c $configuration /p:GeneratePackageOnBuild=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pack AN.CodeAnalyzers
Write-Host "`n[2/3] Packing AN.CodeAnalyzers..." -ForegroundColor Green
dotnet pack "$repoRoot\AN.CodeAnalyzers.csproj" -c $configuration /nodeReuse:false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pack SaferAssemblyLoader
Write-Host "`n[3/3] Packing SaferAssemblyLoader..." -ForegroundColor Green
dotnet pack "$repoRoot\SaferAssemblyLoader\ArtificialNecessity.SaferAssemblyLoader.csproj" -c $configuration /nodeReuse:false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Show only packages deployed during this run (modified after $deployStartTime)
$deployedPackages = Get-ChildItem "$env:LOCAL_NUGET_REPO\*.nupkg" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $deployStartTime } |
    Sort-Object Name
if ($deployedPackages) {
    Write-Host "`nDeployed packages:" -ForegroundColor Cyan
    foreach ($pkg in $deployedPackages) {
        $sizeKB = [math]::Round($pkg.Length / 1024, 1)
        Write-Host "  $($pkg.Name)  (${sizeKB} KB)" -ForegroundColor Green
    }
} else {
    Write-Host "`nWARNING: No packages were deployed to $env:LOCAL_NUGET_REPO" -ForegroundColor Yellow
}
#!/usr/bin/env pwsh
# publish-local.ps1 — Build and pack all packages to local NuGet feed (always stable versions)
#
# Always publishes stable (non-prerelease) version numbers by passing
# /p:NewRelease=true to MSBuild, which auto-increments buildNumberOffset
# in version.json and produces clean versions like 0.1.16.
#
# Usage:
#   ./cmd/publish-local.ps1                    # Debug configuration, stable version
#   ./cmd/publish-local.ps1 -Release           # Release configuration, stable version
#
# Requires: LOCAL_NUGET_REPO environment variable set to local feed path

param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$configuration = if ($Release) { "Release" } else { "Debug" }

Write-Host "=== AN_CodeAnalyzers publish-local ($configuration, stable) ===" -ForegroundColor Cyan

if (-not $env:LOCAL_NUGET_REPO) {
    Write-Host "ERROR: LOCAL_NUGET_REPO environment variable not set." -ForegroundColor Red
    Write-Host 'Set it to your local NuGet feed path, e.g.: $env:LOCAL_NUGET_REPO = "C:\PROJECTS\LocalNuGet"' -ForegroundColor Yellow
    exit 1
}

Write-Host "Local NuGet feed: $env:LOCAL_NUGET_REPO" -ForegroundColor Gray

# Capture timestamp before build/pack so we can identify newly deployed packages
$deployStartTime = Get-Date

# Build the solution
Write-Host "`n[1/3] Building solution..." -ForegroundColor Green
# Note: do NOT pass /p:NewRelease=true here — it would increment buildNumberOffset
# during build AND again during pack (GeneratePackageOnBuild=true produces .nupkg at build time too)
dotnet build "$repoRoot\AN_CodeAnalyzers.sln" -c $configuration /p:GeneratePackageOnBuild=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pack AN.CodeAnalyzers
Write-Host "`n[2/3] Packing AN.CodeAnalyzers..." -ForegroundColor Green
dotnet pack "$repoRoot\AN.CodeAnalyzers.csproj" -c $configuration /p:NewRelease=true /nodeReuse:false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pack SaferAssemblyLoader
Write-Host "`n[3/3] Packing SaferAssemblyLoader..." -ForegroundColor Green
dotnet pack "$repoRoot\SaferAssemblyLoader\ArtificialNecessity.SaferAssemblyLoader.csproj" -c $configuration /p:NewRelease=true /nodeReuse:false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Deploy packages to local feed (NewRelease=true skips the in-project DeployToLocalNuGet target)
Write-Host "`nDeploying packages to local feed..." -ForegroundColor Green
if (-not (Test-Path $env:LOCAL_NUGET_REPO)) {
    New-Item -ItemType Directory -Path $env:LOCAL_NUGET_REPO -Force | Out-Null
}

# Find .nupkg files generated during this run
$packageDirs = @(
    "$repoRoot\bin\$configuration"
    "$repoRoot\SaferAssemblyLoader\bin\$configuration"
)
$newPackages = $packageDirs | ForEach-Object {
    Get-ChildItem "$_\*.nupkg" -ErrorAction SilentlyContinue
} | Where-Object { $_.LastWriteTime -ge $deployStartTime } | Sort-Object Name

if ($newPackages) {
    foreach ($pkg in $newPackages) {
        Copy-Item $pkg.FullName -Destination $env:LOCAL_NUGET_REPO -Force
        $sizeKB = [math]::Round($pkg.Length / 1024, 1)
        Write-Host "  Deployed: $($pkg.Name)  (${sizeKB} KB)" -ForegroundColor Green
    }
    Write-Host "`nDeployed $($newPackages.Count) package(s) to $env:LOCAL_NUGET_REPO" -ForegroundColor Cyan
} else {
    Write-Host "`nWARNING: No .nupkg files found to deploy." -ForegroundColor Yellow
}
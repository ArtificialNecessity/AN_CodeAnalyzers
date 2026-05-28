#!/usr/bin/env -S dotnet run
// publish-local.cs — Build and pack all packages to local NuGet feed
//
// Versioning is timestamp-based (v2) — every build gets a unique version
// automatically via AN.CodeAnalyzers.shared.Build.props. No version files to manage.
// The timestamp is captured once here and passed to MSBuild so all projects
// in the solution get the exact same version (no inter-project skew).
//
// Usage:
//   dotnet cmd/publish-local.cs                    # Debug configuration
//   dotnet cmd/publish-local.cs --release          # Release configuration
//
// Requires: LOCAL_NUGET_REPO environment variable set to local feed path

using System.Diagnostics;

// ─── Parse arguments ────────────────────────────────────────────────────────

bool release = args.Any(a => a.Equals("--release", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("-release", StringComparison.OrdinalIgnoreCase));

string configuration = release ? "Release" : "Debug";

// ─── Resolve repo root (walk up from cwd to find AN_CodeAnalyzers.sln) ──────

string repoRoot = FindRepoRoot(Directory.GetCurrentDirectory())
    ?? throw new InvalidOperationException(
        "Cannot find project root (looked for AN_CodeAnalyzers.sln walking up from cwd)");

string solutionPath = Path.Combine(repoRoot, "AN_CodeAnalyzers.sln");
string codeAnalyzersCsproj = Path.Combine(repoRoot, "src", "AN.CodeAnalyzers.csproj");
string saferAssemblyLoaderCsproj = Path.Combine(repoRoot, "src", "SaferAssemblyLoader", "ArtificialNecessity.SaferAssemblyLoader.csproj");

// ─── Validate LOCAL_NUGET_REPO ──────────────────────────────────────────────

string? localNuGetFeedPath = Environment.GetEnvironmentVariable("LOCAL_NUGET_REPO");
if (string.IsNullOrEmpty(localNuGetFeedPath))
{
    WriteColored("ERROR: LOCAL_NUGET_REPO environment variable not set.", ConsoleColor.Red);
    WriteColored("Set it to your local NuGet feed path, e.g.:", ConsoleColor.Yellow);
    WriteColored("  Linux/macOS: export LOCAL_NUGET_REPO=\"/path/to/LocalNuGet\"", ConsoleColor.Yellow);
    WriteColored("  Windows:     $env:LOCAL_NUGET_REPO = \"C:\\PROJECTS\\LocalNuGet\"", ConsoleColor.Yellow);
    Environment.Exit(1);
}

WriteColored($"\n=== AN_CodeAnalyzers publish-local ({configuration}) ===", ConsoleColor.Cyan);
WriteColored($"Local NuGet feed: {localNuGetFeedPath}", ConsoleColor.DarkGray);

// ─── Capture timestamp ONCE so all projects get the same version ────────────

DateTime now = DateTime.Now;
string buildYYMM   = now.ToString("yyMM");
string buildDDHH   = now.ToString("ddHH");
string buildmmss   = now.ToString("mmss");
string buildYYMMDD = now.ToString("yyMMdd");
string buildHHmmss = now.ToString("HHmmss");

string versionStamp = $"0.{buildYYMM}.{buildDDHH}.{buildmmss}";
string packageStamp = $"0.{buildYYMMDD}.{buildHHmmss}";

string versionProps = $"/p:_BuildYYMM={buildYYMM} /p:_BuildDDHH={buildDDHH} /p:_Buildmmss={buildmmss} /p:_BuildYYMMDD={buildYYMMDD} /p:_BuildHHmmss={buildHHmmss}";

WriteColored($"Version stamp: {versionStamp} (pkg: {packageStamp})", ConsoleColor.DarkGray);

// ─── Capture timestamp before build/pack so we can identify newly deployed packages

DateTime deployStartTime = DateTime.UtcNow;

// ─── Build the solution ─────────────────────────────────────────────────────

WriteColored("\n[1/3] Building solution...", ConsoleColor.Green);
RunOrExit("dotnet", $"build \"{solutionPath}\" -c {configuration} /p:GeneratePackageOnBuild=false {versionProps}");

// ─── Pack AN.CodeAnalyzers ──────────────────────────────────────────────────

WriteColored("\n[2/3] Packing AN.CodeAnalyzers...", ConsoleColor.Green);
RunOrExit("dotnet", $"pack \"{codeAnalyzersCsproj}\" -c {configuration} --no-build /nodeReuse:false {versionProps}");

// ─── Pack SaferAssemblyLoader ───────────────────────────────────────────────

WriteColored("\n[3/3] Packing SaferAssemblyLoader...", ConsoleColor.Green);
RunOrExit("dotnet", $"pack \"{saferAssemblyLoaderCsproj}\" -c {configuration} --no-build /nodeReuse:false {versionProps}");

// ─── Show only packages deployed during this run ────────────────────────────

var deployedPackages = Directory.Exists(localNuGetFeedPath)
    ? new DirectoryInfo(localNuGetFeedPath)
        .GetFiles("*.nupkg")
        .Where(f => f.LastWriteTimeUtc >= deployStartTime)
        .OrderBy(f => f.Name)
        .ToList()
    : [];

if (deployedPackages.Count > 0)
{
    WriteColored("\nDeployed packages:", ConsoleColor.Cyan);
    foreach (var pkg in deployedPackages)
    {
        double sizeKB = Math.Round(pkg.Length / 1024.0, 1);
        WriteColored($"  {pkg.Name}  ({sizeKB} KB)", ConsoleColor.Green);
    }
}
else
{
    WriteColored($"\nWARNING: No packages were deployed to {localNuGetFeedPath}", ConsoleColor.Yellow);
}

// ═════════════════════════════════════════════════════════════════════════════
// Helper methods
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Walk up from <paramref name="startDir"/> to find a directory containing AN_CodeAnalyzers.sln.</summary>
static string? FindRepoRoot(string startDir)
{
    string? dir = startDir;
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "AN_CodeAnalyzers.sln")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}

/// <summary>Write a colored line to the console, restoring the original color afterward.</summary>
static void WriteColored(string message, ConsoleColor color)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(message);
    Console.ForegroundColor = prev;
}

/// <summary>Run a process, inherit stdout/stderr, and exit if it fails.</summary>
static void RunOrExit(string fileName, string arguments)
{
    var psi = new ProcessStartInfo(fileName, arguments)
    {
        UseShellExecute = false,
    };
    using var proc = Process.Start(psi)
        ?? throw new InvalidOperationException($"Failed to start: {fileName} {arguments}");
    proc.WaitForExit();
    if (proc.ExitCode != 0)
    {
        WriteColored($"ERROR: '{fileName} {arguments}' exited with code {proc.ExitCode}", ConsoleColor.Red);
        Environment.Exit(proc.ExitCode);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AN.CodeAnalyzers.ClassLibInfo;

namespace AN.CodeAnalyzers.ClassLibInfo.Tool
{
    internal static class Program
    {
        private static int Main(string[] commandLineArgs)
        {
            if (commandLineArgs.Length < 1)
            {
                PrintUsage();
                return 1;
            }

            // ── parse args ────────────────────────────────────────────────────────
            string? inputDllPath = null;
            string? projectOrSolutionPath = null;
            string? outputPath = null;          // file (dll mode) or directory (project mode)
            string outputFormat = "hjson";
            string docComments = "brief";
            bool includeInternals = false;
            bool includeTransitive = false;

            for (int argIndex = 0; argIndex < commandLineArgs.Length; argIndex++)
            {
                string currentArg = commandLineArgs[argIndex];
                switch (currentArg)
                {
                    case "--include-private-and-internal":
                        includeInternals = true;
                        break;
                    case "--include-transitive":
                        includeTransitive = true;
                        break;
                    case "--format" when argIndex + 1 < commandLineArgs.Length:
                        outputFormat = commandLineArgs[++argIndex];
                        break;
                    case "--doc-comments" when argIndex + 1 < commandLineArgs.Length:
                        docComments = commandLineArgs[++argIndex];
                        break;
                    case "--project" when argIndex + 1 < commandLineArgs.Length:
                        projectOrSolutionPath = commandLineArgs[++argIndex];
                        break;
                    case "--output" when argIndex + 1 < commandLineArgs.Length:
                        outputPath = commandLineArgs[++argIndex];
                        break;
                    default:
                        // bare args: .sln/.csproj → project mode; otherwise input dll then output path
                        if (currentArg.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                            || currentArg.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                            || currentArg.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                            projectOrSolutionPath = currentArg;
                        else if (inputDllPath == null) inputDllPath = currentArg;
                        else if (outputPath == null) outputPath = currentArg;
                        break;
                }
            }

            var dumpOptions = new ApiDumpOptions {
                IncludeInternals = includeInternals,
                OutputFormat = outputFormat,
                DocComments = docComments
            };

            if (projectOrSolutionPath != null)
                return DumpProjectDependencies(projectOrSolutionPath, outputPath, includeTransitive, dumpOptions);

            if (inputDllPath == null)
            {
                PrintUsage();
                return 1;
            }

            string dumpOutput = ApiDumpGenerator.GenerateApiDump(inputDllPath, dumpOptions);
            if (outputPath != null)
            {
                File.WriteAllText(outputPath, dumpOutput);
                Console.Error.WriteLine($"ClassLibInfo: Wrote {outputPath}");
            }
            else
            {
                Console.Write(dumpOutput);
            }
            return 0;
        }

        // ── project/solution mode: dump all resolved NuGet dependencies ──────────────────
        private static int DumpProjectDependencies(
            string projectOrSolutionPath, string? outputDirectory, bool includeTransitive, ApiDumpOptions dumpOptions)
        {
            if (!File.Exists(projectOrSolutionPath))
            {
                Console.Error.WriteLine($"ERROR: not found: {projectOrSolutionPath}");
                return 1;
            }
            outputDirectory ??= Path.Combine("_EXTERNAL_APIS", "ClassLibInfo");
            Directory.CreateDirectory(outputDirectory);

            string nugetPackagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

            List<(string PackageId, string Version)>? resolvedPackages =
                ListResolvedPackages(projectOrSolutionPath, includeTransitive);
            if (resolvedPackages == null) return 1;

            Console.Error.WriteLine($"ClassLibInfo: {resolvedPackages.Count} package(s) resolved from {Path.GetFileName(projectOrSolutionPath)}");
            int failedDumpCount = 0;

            foreach ((string packageId, string packageVersion) in resolvedPackages)
            {
                string packageLibRoot = Path.Combine(nugetPackagesRoot, packageId, packageVersion, "lib");
                if (!Directory.Exists(packageLibRoot))
                {
                    Console.Error.WriteLine($"  (skip {packageId} {packageVersion}: no lib/ — analyzer/meta/tools package)");
                    continue;
                }

                // Prefer the most modern lib target we can find (dumps are surface-equivalent).
                string[] targetFrameworkPreference = { "net10.0", "net9.0", "net8.0", "net6.0", "netstandard2.1", "netstandard2.0" };
                string? chosenFrameworkDirectory = targetFrameworkPreference
                    .Select(tfm => Path.Combine(packageLibRoot, tfm))
                    .FirstOrDefault(Directory.Exists)
                    ?? Directory.GetDirectories(packageLibRoot).OrderByDescending(d => d).FirstOrDefault();
                if (chosenFrameworkDirectory == null)
                {
                    Console.Error.WriteLine($"  (skip {packageId} {packageVersion}: empty lib/)");
                    continue;
                }

                foreach (string assemblyDllPath in Directory.GetFiles(chosenFrameworkDirectory, "*.dll"))
                {
                    string assemblyBaseName = Path.GetFileNameWithoutExtension(assemblyDllPath);
                    string outputFilePath = Path.Combine(outputDirectory, $"{assemblyBaseName}-{packageVersion}.api.txt");
                    if (File.Exists(outputFilePath)) continue; // already dumped (delete to regenerate)
                    try
                    {
                        File.WriteAllText(outputFilePath, ApiDumpGenerator.GenerateApiDump(assemblyDllPath, dumpOptions));
                        Console.Error.WriteLine($"  {Path.GetFileName(assemblyDllPath),-44} -> {outputFilePath}");
                    }
                    catch (Exception dumpException)
                    {
                        Console.Error.WriteLine($"  ERROR dumping {assemblyDllPath}: {dumpException.Message}");
                        failedDumpCount++;
                    }
                }
            }

            Console.Error.WriteLine($"ClassLibInfo: Done. API dumps in {outputDirectory}{Path.DirectorySeparatorChar}");
            return failedDumpCount == 0 ? 0 : 1;
        }

        /// <summary>Runs `dotnet list <path> package --format json` and extracts resolved package ids/versions.</summary>
        private static List<(string PackageId, string Version)>? ListResolvedPackages(
            string projectOrSolutionPath, bool includeTransitive)
        {
            var listStartInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            listStartInfo.ArgumentList.Add("list");
            listStartInfo.ArgumentList.Add(projectOrSolutionPath);
            listStartInfo.ArgumentList.Add("package");
            listStartInfo.ArgumentList.Add("--format");
            listStartInfo.ArgumentList.Add("json");
            if (includeTransitive) listStartInfo.ArgumentList.Add("--include-transitive");

            using var listProcess = Process.Start(listStartInfo);
            if (listProcess == null)
            {
                Console.Error.WriteLine("ERROR: could not start 'dotnet list package'");
                return null;
            }
            string jsonOutput = listProcess.StandardOutput.ReadToEnd();
            listProcess.WaitForExit();
            if (listProcess.ExitCode != 0)
            {
                Console.Error.WriteLine($"ERROR: 'dotnet list package' exit {listProcess.ExitCode} (is the project restored?)");
                return null;
            }

            // Shape: { projects: [ { frameworks: [ { topLevelPackages: [ {id, resolvedVersion} ], transitivePackages: [...] } ] } ] }
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using JsonDocument doc = JsonDocument.Parse(jsonOutput);
            if (doc.RootElement.TryGetProperty("projects", out JsonElement projects))
            {
                foreach (JsonElement project in projects.EnumerateArray())
                {
                    if (!project.TryGetProperty("frameworks", out JsonElement frameworks)) continue;
                    foreach (JsonElement framework in frameworks.EnumerateArray())
                    {
                        foreach (string packageListName in new[] { "topLevelPackages", "transitivePackages" })
                        {
                            if (!framework.TryGetProperty(packageListName, out JsonElement packageList)) continue;
                            foreach (JsonElement package in packageList.EnumerateArray())
                            {
                                string? id = package.GetProperty("id").GetString();
                                string? version = package.GetProperty("resolvedVersion").GetString();
                                if (id != null && version != null)
                                    resolved[id.ToLowerInvariant()] = version; // last-wins; versions should agree
                            }
                        }
                    }
                }
            }

            // Skip our own tooling packages (no lib/ assemblies worth dumping).
            resolved.Remove("artificialnecessity.codeanalyzers");

            return resolved.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  ClassLibInfo <input.dll> [output.api.txt] [options]");
            Console.Error.WriteLine("  ClassLibInfo --project <path.csproj|.sln> [--output <dir>] [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Project mode dumps every NuGet dependency resolved by the project/solution");
            Console.Error.WriteLine("(via 'dotnet list package'; requires a prior restore) into --output");
            Console.Error.WriteLine("(default: _EXTERNAL_APIS/ClassLibInfo/) as {Assembly}-{version}.api.txt.");
            Console.Error.WriteLine("Existing dump files are kept — delete them to regenerate.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --include-private-and-internal  Include all private/internal members (default: public+protected only)");
            Console.Error.WriteLine("  --doc-comments none|brief|full  Doc comment extraction (default: brief)");
            Console.Error.WriteLine("  --format hjson|flat             Output format (default: hjson; use flat for grep-friendly text)");
            Console.Error.WriteLine("  --include-transitive            Project mode: also dump transitive NuGet dependencies");
        }
    }
}
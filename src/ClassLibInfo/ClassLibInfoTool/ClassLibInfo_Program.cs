using System;
using System.IO;
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

            string inputDllPath = commandLineArgs[0];
            string outputFormat = "hjson";
            string? outputFilePath = null;
            bool includeInternals = false;

            // Parse optional args
            for (int argIndex = 1; argIndex < commandLineArgs.Length; argIndex++)
            {
                string currentArg = commandLineArgs[argIndex];
                if (currentArg == "--include-private-and-internal")
                {
                    includeInternals = true;
                }
                else if (currentArg == "--format" && argIndex + 1 < commandLineArgs.Length)
                {
                    outputFormat = commandLineArgs[++argIndex];
                }
                else if (outputFilePath == null)
                {
                    outputFilePath = currentArg;
                }
            }

            var dumpOptions = new ApiDumpOptions {
                IncludeInternals = includeInternals,
                OutputFormat = outputFormat
            };
            string hjsonOutput = ApiDumpGenerator.GenerateApiDump(inputDllPath, dumpOptions);

            if (outputFilePath != null)
            {
                File.WriteAllText(outputFilePath, hjsonOutput);
                Console.Error.WriteLine($"ClassLibInfo: Wrote {outputFilePath}");
            }
            else
            {
                Console.Write(hjsonOutput);
            }

            return 0;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  ClassLibInfo <input.dll> <output.api.txt> [options]");
            Console.Error.WriteLine("  ClassLibInfo --batch <manifest.txt> --output <dir> [options]");
            Console.Error.WriteLine("  ClassLibInfo --project <path.csproj> --output <dir> [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --include-private-and-internal  Include all private/internal members (default: public+protected only)");
            Console.Error.WriteLine("  --doc-comments none|brief|full  Doc comment extraction (default: brief)");
            Console.Error.WriteLine("  --include-transitive       Include transitive NuGet dependencies");
            Console.Error.WriteLine("  --framework <tfm>          Target framework for multi-targeting projects");
        }
    }
}
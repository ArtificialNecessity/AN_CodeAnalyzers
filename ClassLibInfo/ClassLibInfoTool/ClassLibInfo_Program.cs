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
            string visibilityScope = "public";
            string outputFormat = "hjson";
            string? outputFilePath = null;

            // Parse optional args
            for (int argIndex = 1; argIndex < commandLineArgs.Length; argIndex++)
            {
                string currentArg = commandLineArgs[argIndex];
                if (currentArg == "--visibility" && argIndex + 1 < commandLineArgs.Length)
                {
                    visibilityScope = commandLineArgs[++argIndex];
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
                VisibilityScope = visibilityScope,
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
            Console.Error.WriteLine("  --visibility public|all    Visibility scope (default: public)");
            Console.Error.WriteLine("  --doc-comments none|brief|full  Doc comment extraction (default: brief)");
            Console.Error.WriteLine("  --include-transitive       Include transitive NuGet dependencies");
            Console.Error.WriteLine("  --framework <tfm>          Target framework for multi-targeting projects");
        }
    }
}
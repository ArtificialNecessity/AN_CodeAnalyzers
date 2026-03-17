using System;
using AN.CodeAnalyzers;

namespace AN.CodeAnalyzers.JsonPeekTool
{
    internal static class Program
    {
        private static int Main(string[] commandLineArgs)
        {
            if (commandLineArgs.Length < 2) {
                Console.Error.WriteLine("Usage: JsonPeek <file> <key-path>");
                Console.Error.WriteLine();
                Console.Error.WriteLine("  file       Path to a JSON, JSONC, or HJSON file");
                Console.Error.WriteLine("  key-path   Dot-separated key path (e.g. 'version' or 'parent.child.key')");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Examples:");
                Console.Error.WriteLine("  JsonPeek config.json version");
                Console.Error.WriteLine("  JsonPeek package.json dependencies.Hjson");
                return 1;
            }

            string inputFilePath = commandLineArgs[0];
            string queryKeyPath = commandLineArgs[1];

            string extractedValue = JsonPeekParser.ReadValueFromFile(inputFilePath, queryKeyPath);
            Console.WriteLine(extractedValue);
            return 0;
        }
    }
}
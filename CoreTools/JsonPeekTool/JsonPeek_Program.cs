using System;
using AN.CodeAnalyzers;

namespace AN.CodeAnalyzers.JsonPeekTool
{
    internal static class Program
    {
        private static int Main(string[] commandLineArgs)
        {
            if (commandLineArgs.Length < 2) {
                PrintUsage();
                return 1;
            }

            string firstArg = commandLineArgs[0];

            // Handle --write-value flag
            if (firstArg == "--write-value") {
                if (commandLineArgs.Length != 4) {
                    Console.Error.WriteLine("Error: --write-value requires exactly 3 arguments");
                    Console.Error.WriteLine();
                    PrintUsage();
                    return 1;
                }
                string targetFilePath = commandLineArgs[1];
                string targetKeyPath = commandLineArgs[2];
                string valueToWrite = commandLineArgs[3];
                JsonPeekParser.WriteValueToFile(targetFilePath, targetKeyPath, valueToWrite);
                Console.WriteLine($"Updated {targetKeyPath} in {targetFilePath}");
                return 0;
            }

            // Handle --inc-integer flag
            if (firstArg == "--inc-integer") {
                if (commandLineArgs.Length < 3 || commandLineArgs.Length > 4) {
                    Console.Error.WriteLine("Error: --inc-integer requires 2 or 3 arguments");
                    Console.Error.WriteLine();
                    PrintUsage();
                    return 1;
                }
                string targetFilePath = commandLineArgs[1];
                string targetKeyPath = commandLineArgs[2];
                int incrementAmount = 1;
                if (commandLineArgs.Length == 4) {
                    if (!int.TryParse(commandLineArgs[3], out incrementAmount)) {
                        Console.Error.WriteLine($"Error: increment amount '{commandLineArgs[3]}' is not a valid integer");
                        return 1;
                    }
                }
                int newValue = JsonPeekParser.IncrementIntegerInFile(targetFilePath, targetKeyPath, incrementAmount);
                Console.WriteLine(newValue);
                return 0;
            }

            // Handle --add-float flag
            if (firstArg == "--add-float") {
                if (commandLineArgs.Length != 4) {
                    Console.Error.WriteLine("Error: --add-float requires exactly 3 arguments");
                    Console.Error.WriteLine();
                    PrintUsage();
                    return 1;
                }
                string targetFilePath = commandLineArgs[1];
                string targetKeyPath = commandLineArgs[2];
                if (!double.TryParse(commandLineArgs[3], out double addAmount)) {
                    Console.Error.WriteLine($"Error: add amount '{commandLineArgs[3]}' is not a valid number");
                    return 1;
                }
                double newValue = JsonPeekParser.AddToFloatInFile(targetFilePath, targetKeyPath, addAmount);
                Console.WriteLine(newValue);
                return 0;
            }

            // Default: read value
            if (commandLineArgs.Length != 2) {
                Console.Error.WriteLine("Error: read mode requires exactly 2 arguments");
                Console.Error.WriteLine();
                PrintUsage();
                return 1;
            }

            string inputFilePath = commandLineArgs[0];
            string queryKeyPath = commandLineArgs[1];

            string extractedValue = JsonPeekParser.ReadValueFromFile(inputFilePath, queryKeyPath);
            Console.WriteLine(extractedValue);
            return 0;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  JsonPeek <file> <key-path>                           # Read value");
            Console.Error.WriteLine("  JsonPeek --write-value <file> <key-path> <value>     # Write value");
            Console.Error.WriteLine("  JsonPeek --inc-integer <file> <key-path> [amount]    # Increment integer");
            Console.Error.WriteLine("  JsonPeek --add-float <file> <key-path> <amount>      # Add to float");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Arguments:");
            Console.Error.WriteLine("  file       Path to a JSON, JSONC, or HJSON file");
            Console.Error.WriteLine("  key-path   Dot-separated key path (e.g. 'version' or 'parent.child.key')");
            Console.Error.WriteLine("  value      New value to write (will be parsed as JSON)");
            Console.Error.WriteLine("  amount     Amount to increment/add (can be negative)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  JsonPeek config.json version");
            Console.Error.WriteLine("  JsonPeek --write-value config.json version \"1.2.3\"");
            Console.Error.WriteLine("  JsonPeek --inc-integer version.json buildNumberOffset");
            Console.Error.WriteLine("  JsonPeek --inc-integer version.json buildNumberOffset 5");
            Console.Error.WriteLine("  JsonPeek --add-float config.json price 0.5");
            Console.Error.WriteLine("  JsonPeek --add-float config.json discount -0.1");
        }
    }
}
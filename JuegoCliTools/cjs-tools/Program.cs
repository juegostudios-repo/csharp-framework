using System;
using System.Threading.Tasks;
using System.Linq;

namespace CjsTools;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("The 'cjs' is a dotnet CLI tool for creating Juego projects and JWT secrets.");
            Console.WriteLine("Display tool options with:");
            Console.WriteLine("cjs -h");
            return 1;
        }

        if (args[0].Equals("-h", StringComparison.CurrentCultureIgnoreCase) || args[0].Equals("--help", StringComparison.CurrentCultureIgnoreCase) || args[0].Equals("-?", StringComparison.CurrentCultureIgnoreCase))
        {
            Console.WriteLine("Usage: cjs <command> [options]");
            Console.WriteLine("Commands:");
            Console.WriteLine("  project    Create a new project.");
            Console.WriteLine("  jwt-up     Update JWT secret in appsettings.json.");
            return 1;
        }

        var command = args[0];
        var commandArgs = args.Skip(1).ToArray();

        switch (command)
        {
            case "project":
                return ProjectCli.CreateProject(commandArgs);
            case "jwt-up":
                return await JwtCli.UpdateJwt(commandArgs.Length > 0 ? commandArgs[0] : "appsettings.json");
            default:
                Console.WriteLine($"Unknown command: {command}");
                return 1;
        }
    }
}

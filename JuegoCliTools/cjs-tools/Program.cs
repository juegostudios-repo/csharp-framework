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
            Logger.Log("The 'cjs' is a dotnet CLI tool for creating Juego projects and JWT secrets.");
            Logger.Log("Display tool options with:");
            Logger.Log("cjs -h");
            return 1;
        }

        if (args[0].Equals("-h", StringComparison.CurrentCultureIgnoreCase) || args[0].Equals("--help", StringComparison.CurrentCultureIgnoreCase) || args[0].Equals("-?", StringComparison.CurrentCultureIgnoreCase))
        {
            Logger.Log("Usage: cjs <command> [options]");
            Logger.Log("Commands:");
            Logger.Log("  project    Create a new project.");
            Logger.Log("  jwt-up     Update JWT secret in appsettings.json.");
            Logger.Log("  model      Create or modify a database model.");
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
            case "model":
                return ModelCli.HandleModelCommand(commandArgs);
            default:
                Logger.Error($"Unknown command: {command}");
                return 1;
        }
    }
}

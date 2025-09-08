using System;
using System.Diagnostics;
using System.IO;

namespace CjsTools;

public class ProjectCli
{
    public static int CreateProject(string[] args)
    {
        // Example: cjs project -n test --> dotnet new juegoframework-project -n test
        if (args.Length == 0)
        {
            Logger.Log("The 'cjs project' is a dotnet CLI tool for creating Juego projects.");
            Logger.Log("Display tool options with:");
            Logger.Log("cjs project -h");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "new juegoframework-project " + string.Join(" ", args),
            RedirectStandardOutput = false,
            UseShellExecute = true
        };
        var process = Process.Start(psi);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Logger.Error("Failed to create the project. Please check the arguments and try again.");
            return process.ExitCode;
        }


        if (Array.IndexOf(args, "-n") == -1 || Array.IndexOf(args, "-n") + 1 >= args.Length)
        {
            return 1;
        }

        Logger.Info("Project created successfully!");
        var projectName = args[Array.IndexOf(args, "-n") + 1];
        Logger.Info($"Project '{projectName ?? ""}' created successfully!");

        Logger.Log("Now updating JWT secret in appsettings.json...");


        var projectDirectory = Path.Combine(Directory.GetCurrentDirectory(), projectName);
        if (!Directory.Exists(projectDirectory))
        {
            Logger.Error($"Project directory '{projectDirectory}' does not exist.");
            return 1;
        }
        Directory.SetCurrentDirectory(projectDirectory);

        var updateResult = JwtCli.UpdateJwt(Path.Combine(projectDirectory, "appsettings.json")).Result;
        if (updateResult != 0)
        {
            Logger.Error("Failed to update JWT secret. Please check the arguments and try again.");
        }
        return updateResult;
    }
}

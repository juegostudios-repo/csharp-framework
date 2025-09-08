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

        // Check if JuegoFramework.Templates is installed
        var templateInstalled = false;
        var psiList = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "new --list",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using (var processList = Process.Start(psiList))
        {
            var output = processList.StandardOutput.ReadToEnd();
            processList.WaitForExit();
            if (output.Contains("juegoframework-project"))
            {
                templateInstalled = true;
            }
        }

        if (!templateInstalled)
        {
            Logger.Log("JuegoFramework.Templates is not installed. Install it now? (y/n)");

            var key = Console.ReadKey();
            Logger.Log();
            if (key.KeyChar == 'y' || key.KeyChar == 'Y')
            {
                var psiInstall = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "new -i JuegoFramework.Templates",
                    RedirectStandardOutput = false,
                    UseShellExecute = true
                };
                var processInstall = Process.Start(psiInstall);
                processInstall.WaitForExit();
                if (processInstall.ExitCode != 0)
                {
                    Logger.Error("Failed to install JuegoFramework.Templates.");
                    return processInstall.ExitCode;
                }
            }
            else
            {
                Logger.Error("Cannot continue without JuegoFramework.Templates.");
                return 1;
            }
        }
        else
        {
            Logger.Log("Checking for updates to JuegoFramework.Templates...");
            var psiUpdateCheck = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = " new update --check-only",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using (var processUpdateCheck = Process.Start(psiUpdateCheck))
            {
                var updateOutput = processUpdateCheck.StandardOutput.ReadToEnd();
                processUpdateCheck.WaitForExit();
                if (!updateOutput.Contains("packages are up-to-date") && updateOutput.Contains("available") && updateOutput.Contains("JuegoFramework.Templates"))
                {
                    Logger.Warning("An update for JuegoFramework.Templates is available. Update now? (y/n)");
                    var key = Console.ReadKey();
                    Logger.Log();
                    if (key.KeyChar == 'y' || key.KeyChar == 'Y')
                    {
                        var psiUpdate = new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = "new --update-apply JuegoFramework.Templates",
                            RedirectStandardOutput = false,
                            UseShellExecute = true
                        };
                        var processUpdate = Process.Start(psiUpdate);
                        processUpdate.WaitForExit();
                        if (processUpdate.ExitCode != 0)
                        {
                            Logger.Error("Failed to update JuegoFramework.Templates.");
                            return processUpdate.ExitCode;
                        }
                    }
                }
            }
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

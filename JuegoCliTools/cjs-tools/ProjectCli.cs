using System;
using System.Diagnostics;
using System.IO;

namespace CjsTools;

public class ProjectCli
{
  // Class implementation goes here
  public static int CreateProject(string[] args)
  {
    // Example: project -n test --> dotnet new webapi -n test
    if (args.Length == 0)
    {
      Console.WriteLine("The 'cjs project' is a dotnet CLI tool for creating Juego projects.");
      Console.WriteLine("Display tool options with:");
      Console.WriteLine("cjs project -h");
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
      Console.WriteLine("Failed to create the project. Please check the arguments and try again.");
      return process.ExitCode;
    }


    if (Array.IndexOf(args, "-n") == -1 || Array.IndexOf(args, "-n") + 1 >= args.Length)
    {
      return 1;
    }

    Console.WriteLine("Project created successfully!");
    var projectName = args[Array.IndexOf(args, "-n") + 1];
    Console.WriteLine($"Project '{projectName ?? ""}' created successfully!");

    Console.WriteLine("Now updating JWT secret in appsettings.json...");


    var projectDirectory = Path.Combine(Directory.GetCurrentDirectory(), projectName);
    if (!Directory.Exists(projectDirectory))
    {
      Console.WriteLine($"Project directory '{projectDirectory}' does not exist.");
      return 1;
    }
    Directory.SetCurrentDirectory(projectDirectory);
    Console.WriteLine($"Current Directory: {Directory.GetCurrentDirectory()}");

    // Directly call the UpdateJwt logic
    var updateResult = JwtCli.UpdateJwt(Path.Combine(projectDirectory, "appsettings.json")).Result;
    if (updateResult != 0)
    {
      Console.WriteLine("Failed to update JWT secret. Please check the arguments and try again.");
    }
    return updateResult;
  }
}

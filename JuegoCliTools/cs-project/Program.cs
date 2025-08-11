using System.Diagnostics;
using System;
using System.IO;
using System.Text.Json;

internal class Program
{
  static int Main(string[] args)
  {
    // Example: project -n test --> dotnet new webapi -n test
    if (args.Length == 0)
    {
      Console.WriteLine("Please provide the project name as an argument.");
      return 1;
    }
    Console.WriteLine("Arguments received: " + JsonSerializer.Serialize(args));

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

    psi = new ProcessStartInfo
    {
      FileName = "jwt-up",
      Arguments = "",
      RedirectStandardOutput = false,
      UseShellExecute = true
    };
    process = Process.Start(psi);
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
      Console.WriteLine("Failed to update JWT secret. Please check the arguments and try again.");
      return process.ExitCode;
    }
    return process.ExitCode;
  }
}
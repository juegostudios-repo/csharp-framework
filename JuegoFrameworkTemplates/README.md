JuegoFramework templates to create ASP.NET project and controller with DTOs

Prerequisites to create/update template package
 - Install .NET 8.
 - Install the Microsoft.TemplateEngine.Authoring.Templates template from the NuGet package. 
   `dotnet new install Microsoft.TemplateEngine.Authoring.Templates::9.0.200`

Build template:
dotnet pack

Install package
dotnet new install bin/Release/JuegoFramework.Templates.1.0.0.nupkg
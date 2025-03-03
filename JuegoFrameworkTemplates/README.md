## JuegoFramework Templates
JuegoFramework templates to create ASP.NET project, controller with DTOs and library file.

### Library Template

The JuegoFramework Library Template allows you to quickly generate library files for your project. You can create either SQL library files or Utility library files using this template.

**Usage:**

  `dotnet new juegoframework-library [options] [template options]`

Options:
  - `-n, --name` : The name for the output being created; defaults to the directory name if not specified.

Template options:


  - `-p, --project` :  Namespace for the generated code. Default: API.

  - `-li, --libType <sql|util>` :  Type of library file to create. Default: sql

**Example:**

`dotnet new juegoframework-library -n User -p API -li sql`

Above command creates a SQL library file `UserLib.cs` in `Library/SqlLib` and a model file `User.cs` in `Models` folder, with the `API` namespace.

### Development
Prerequisites to create/update template package
 - Install .NET 8.
 - Install the Microsoft.TemplateEngine.Authoring.Templates template from the NuGet package. 
   `dotnet new install Microsoft.TemplateEngine.Authoring.Templates::9.0.200`

Build template:
dotnet pack

Install package
dotnet new install bin/Release/JuegoFramework.Templates.1.0.0.nupkg
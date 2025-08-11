# Juego CLI Tools

A collection of .NET CLI tools for project scaffolding and JWT secret management.

## Tools Included

### 1. Project Cli (`cs-project`)
Creates new projects using the `juegoframework-project` template and automatically updates JWT secrets. Note: JWT Updater should be installed globally.

### 2. JWT Updater (`jwt-up`)
Updates JWT secrets in `appsettings.json` files.


## Development

### Building
```bash
dotnet build
```

### Run
```bash
# for JWT Updater
dotnet run
# for Project Cli.
dotnet run -n <project name> <template options>
```

## Installation

### Prerequisites
- .NET 8.0 SDK 
- `juegoframework-project` template installed

### Install Templates (if needed)
```bash
dotnet new install JuegoFramework.Templates
```

### Install Tools Locally (Tool development)

1. navigate to the tool directory and pack the tools:
```bash
cd jwt-updater
dotnet pack
```

2. Install tool
```bash
# Install Project-Cli locally
dotnet tool install --add-source ./bin/Release cs-project
# Install JWT-Updater locally
dotnet tool install --add-source ./bin/Release jwt-updater
```

3. Restore tools from manifest:
```bash
dotnet tool restore
```

4. Use tools with `dotnet` prefix:
```bash
dotnet jwt-up
dotnet cs-project -n MyProject
```

### Install Tools Globally

1. navigate to the tool directory and pack the tools:
```bash
cd jwt-updater
dotnet pack
```

2. Install globally:
```bash
# Install Project-Cli globally
cd cs-project
dotnet tool install --global --add-source ./bin/Release cs-project

# Install JWT-Updater globally
cd jwt-updater  
dotnet tool install --global --add-source ./bin/Release jwt-updater
```

3. Add to PATH (if prompted):
```bash
export PATH="$PATH:/home/$USER/.dotnet/tools"
```

4. Use tools directly:
```bash
cs-project -n MyProject
jwt-up MyProject
```

## Usage

### Creating a New Project
```bash
# Local installation
dotnet cs-project -n MyProjectName

# Global installation
cs-project -n MyProjectName
```

This command:
1. Creates a new project using `dotnet new juegoframework-project -n MyProjectName`
2. Automatically updates JWT secret in `appsettings.json` by running `jwt-up`

### Updating JWT Secret Only
```bash
# Local installation (run from project directory containing appsettings.json)
dotnet jwt-up

# Global installation (run from project directory containing appsettings.json)
jwt-up
```

## Tool Manifest

The tools are configured in `.config/dotnet-tools.json`:
Update tool version on each release
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "jwt-updater": {
      "version": "0.0.1",
      "commands": [
        "jwt-up"
      ]
    },
    "cs-project": {
      "version": "0.0.1",
      "commands": [
        "cs-project"
      ]
    }
  }
}
```

## Troubleshooting

### Tool Not Found
If you get "command not found" errors:

1. For local tools, use `dotnet` prefix:
```bash
dotnet cs-project -n MyProject
```

2. For global tools, ensure PATH is set:
```bash
export PATH="$PATH:/home/$USER/.dotnet/tools"
```

### appsettings.json Not Found
The JWT updater looks for `appsettings.json` in the current directory. Make sure you're running the command from the correct project directory.

### Template Not Found
Install the required project template:
```bash
dotnet new install JuegoFramework.Templates
```
# Juego CLI Tools (`cjs`)

A collection of .NET CLI tools for project scaffolding and JWT secret management.

## Tools Included

### 1. Project Cli (`project`)
Creates new projects using the `juegoframework-project` template and automatically updates JWT secrets.

### 2. JWT Updater (`jwt-up`)
Updates JWT secrets in `appsettings.json` files.

### 3. Database model creater (`model`)
Create Database model class with primary key and timestamps. (`create`)
Add column to model class with given data type. (`add-column`)


## Development

### Building
```bash
cd cjs-tools
dotnet build
```

### Run
```bash
# for JWT Updater
dotnet run jwt-up
# for Project Cli
dotnet run project -n <project name> <template options>
# for Model Cli
dotnet run model create -n <table_name>
dotnet run model add-column -n <column_name> -t <table_name> -d <data_type> 
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
cd cjs-tools
dotnet pack
```

2. Install tool
```bash
dotnet tool install --add-source ./bin/Release cjs-tools
```

3. Restore tools from manifest:
```bash
dotnet tool restore
```

4. Use tools with `dotnet` prefix:
```bash
dotnet cjs jwt-up 
dotnet cjs project -h
dotnet cjs model create -n <table_name>
dotnet cjs model add-column -n <column_name> -t <table_name> -d <data_type> 
```

### Install Tools Globally

1. navigate to the tool directory and pack the tools:
```bash
cd cjs-tools
dotnet pack
```

2. Install globally:
```bash
dotnet tool install --global --add-source ./bin/Release cjs-tools
```

3. Add to PATH (if prompted):
```bash
export PATH="$PATH:/home/$USER/.dotnet/tools"
```

4. Use tools directly:
```bash
cjs project -h
cjs jwt-up
cjs model create -n <table_name>
cjs model add-column -n <column_name> -t <table_name> -d <data_type> 
```

## Usage

### Creating a New Project
```bash
# Local installation
dotnet cjs project -n MyProjectName

# Global installation
cjs project -n MyProjectName -db MySql
```

This command:
1. Creates a new project using `dotnet new juegoframework-project -n MyProjectName`
2. Updates JWT secret in `appsettings.json`

### Updating JWT Secret Only
```bash
# Local installation (run from project directory containing appsettings.json)
dotnet cjs jwt-up 

# Global installation (run from project directory containing appsettings.json)
cjs jwt-up
```

## Tool Manifest

The tools are configured in `.config/dotnet-tools.json`:
Update tool version on each release
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "cjs-tools": {
      "version": "0.0.1",
      "commands": [
        "cjs"
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
dotnet cjs project -n MyProject
```

2. For global tools, ensure PATH is set:
```bash
export PATH="$PATH:/home/$USER/.dotnet/tools"
```

### `appsettings.json` Not Found

The JWT updater searches for an `appsettings.json` file in your current working directory. If the file is missing or located elsewhere, ensure you are running the command from the correct project folder.

Alternatively, you can specify the path to your `appsettings.json` file directly as an argument:

```bash
# Specify a custom path to appsettings.json
cjs jwt-up ./path/to/appsettings.json
```

This allows you to update the JWT secret in any json file.

### Template Not Found
Install the required project template:
```bash
dotnet new install JuegoFramework.Templates
```
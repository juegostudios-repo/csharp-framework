To get dotnet ef:
```sh
dotnet tool uninstall --global dotnet-ef
dotnet tool install --global dotnet-ef --version 8.0.1
```

To create a migration:
```sh
dotnet ef migrations add my_migration_name
```

To run migrations:
```sh
dotnet ef database update
```

Build docker image:
```sh
docker build -t example-project .
```

Run docker image:
```sh
docker run -p 5000:5000 example-project
```

Run docker image in development mode:
```sh
docker run -e ASPNETCORE_ENVIRONMENT=Development -p 5000:5000 example-project
```

Run docker image as cron job:
```sh
docker run -e MODE=CRON -p 5000:5000 example-project
```

To get dotnet-outdated-tool:
```sh
dotnet tool install --global dotnet-outdated-tool
```

To update dotnet packages with the latest version:
```sh
dotnet outdated --version-lock major --upgrade
```

Available environment variables:
```
ASPNETCORE_ENVIRONMENT = Development | Don't set

MODE = CRON | Don't set

USE_WEBSOCKET_SYSTEM = "SERVER" | "AWS" | "AZURE" | Don't set (will be treated as if SERVER is set)

AWS_WEBSOCKET_ENDPOINT = "https://aws-websocket-api-endpoint" | Don't set | Required if USE_WEBSOCKET_SYSTEM = "AWS"
AWS_REGION = "ap-south-1" | Don't set | Required if USE_WEBSOCKET_SYSTEM = "AWS"

AZURE_WEBSOCKET_ENDPOINT = "https://azure-websocket-api-endpoint" | Don't set | Required if USE_WEBSOCKET_SYSTEM = "AZURE"
AZURE_WEBSOCKET_ACCESS_TOKEN = "xyz" | Don't set | Required if USE_WEBSOCKET_SYSTEM = "AZURE"
AZURE_WEBSOCKET_HUB = "xyz" | Don't set | Required if USE_WEBSOCKET_SYSTEM = "AZURE"
```

Serverless Deployment
```sh
dotnet lambda deploy-serverless example-project-000
```

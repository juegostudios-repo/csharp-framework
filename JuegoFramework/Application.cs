using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using JuegoFramework.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

public static class Application
{
    public static void InitLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware", Serilog.Events.LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.With<RequestIdEnricher>()
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {RequestId}{Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static async Task InitCron()
    {
        if (Environment.GetEnvironmentVariable("MODE") == "CRON")
        {
            await CronJobService.Start();
        }
    }

    public static WebApplicationBuilder InitBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddScoped<WebSocketService>();

        builder.Services.AddScoped<UserAuth>();

        builder.Services.AddControllers();

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen();

        builder.Services.AddHealthChecks()
            .AddMySql(
                connectionString: builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new Exception("Database ConnectionString not defined"),
                name: "MySql",
                failureStatus: HealthStatus.Unhealthy);

        builder.Host.UseSerilog();

        builder.Services.AddHttpLogging(logging =>
        {
            logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
            logging.CombineLogs = true;
        });

        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = actionContext =>
            {
                var missingField = actionContext.ModelState
                .Where(ms => ms.Value?.Errors.Count > 0)
                .Select(ms => ms.Value?.Errors.First())
                .FirstOrDefault()?.ErrorMessage.Split(":").Last().Trim() ?? "";

                if (missingField.EndsWith("A non-empty request body is required."))
                {
                    var modelType = actionContext.ActionDescriptor.Parameters.FirstOrDefault()?.ParameterType;
                    if (modelType != null)
                    {
                        var properties = modelType.GetProperties();
                        var missingFields = properties
                            .Where(p => p.GetCustomAttribute<RequiredAttribute>() != null)
                            .Select(p => Regex.Replace(p.Name, "(?<!^)([A-Z])", "_$1").ToLower())
                            .ToList();

                        return ApiResponse.setResponse("PARAMETER_IS_MANDATORY", new { }, string.Join(", ", missingFields));
                    }
                }

                if (missingField.EndsWith("is required."))
                {
                    var missingField2 = actionContext.ModelState
                    .Where(ms => ms.Value?.Errors.Count > 0)
                    .Select(ms => ms.Key)
                    .FirstOrDefault() ?? "";

                    missingField2 = Regex.Replace(missingField2, "(?<!^)([A-Z])", "_$1").ToLower();

                    return ApiResponse.setResponse("INVALID_INPUT_EMPTY", new { }, missingField2);
                }

                return ApiResponse.setResponse("PARAMETER_IS_MANDATORY", new { }, string.Join(", ", missingField));
            };
        });

        builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

        var dbConnectionStringEnv = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(dbConnectionStringEnv))
        {
            builder.Configuration["ConnectionStrings:DefaultConnection"] = dbConnectionStringEnv;
        }

        return builder;
    }

    public static WebApplication InitApp(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "SERVER")
        {
            app.UseWebSockets();
            app.UseMiddleware<WebSocketMiddleware>();
        }

        app.UseMiddleware<RequestMiddleware>();

        app.UseHttpLogging();

        app.MapControllers();

        app.MapHealthChecks("/health");

        Global.ServiceProvider = app.Services.GetRequiredService<IServiceProvider>();
        Global.EndpointSources = app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
        Global.Configuration = app.Configuration;
        Global.Environment = app.Environment;
        Global.BaseResponse = JsonSerializer.Deserialize<Dictionary<string, ResponseJson>>(File.ReadAllText(@"Globals/response.json")) ?? throw new Exception("Error reading Globals/response.json file");
        Global.ConnectionString = app.Configuration.GetConnectionString("DefaultConnection");

        if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "SERVER")
        {
            if (Environment.GetEnvironmentVariable("SERVER_WEBSOCKET_HTTP_PORT") == null)
            {
                throw new Exception("SERVER_WEBSOCKET_HTTP_PORT is not defined in the environment variables");
            }

            if (Environment.GetEnvironmentVariable("WEBSOCKET_URL") == null)
            {
                throw new Exception("WEBSOCKET_URL is not defined in the environment variables");
            }
        }

        if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "AWS")
        {
            if (Environment.GetEnvironmentVariable("AWS_WEBSOCKET_ENDPOINT") == null)
            {
                throw new Exception("AWS_WEBSOCKET_ENDPOINT is not defined in the environment variables");
            }

            if (Environment.GetEnvironmentVariable("AWS_REGION") == null)
            {
                throw new Exception("AWS_REGION is not defined in the environment variables");
            }

            if (Environment.GetEnvironmentVariable("WEBSOCKET_URL") == null)
            {
                throw new Exception("WEBSOCKET_URL is not defined in the environment variables");
            }
        }

        if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "AZURE")
        {
            if (Environment.GetEnvironmentVariable("AZURE_WEBSOCKET_ENDPOINT") == null)
            {
                throw new Exception("AZURE_WEBSOCKET_ENDPOINT is not defined in the environment variables");
            }

            if (Environment.GetEnvironmentVariable("AZURE_WEBSOCKET_ACCESS_TOKEN") == null)
            {
                throw new Exception("AZURE_WEBSOCKET_ACCESS_TOKEN is not defined in the environment variables");
            }

            if (Environment.GetEnvironmentVariable("AZURE_WEBSOCKET_HUB") == null)
            {
                throw new Exception("AZURE_WEBSOCKET_HUB is not defined in the environment variables");
            }
        }

        return app;
    }
}

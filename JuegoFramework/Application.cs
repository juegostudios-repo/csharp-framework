using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using JuegoFramework.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

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

        builder.Services.AddControllers(options =>
        {
            options.ModelMetadataDetailsProviders.Add(new RequiredBindingMetadataProvider());
        });

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen();

        var dbConnectionStringEnv = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(dbConnectionStringEnv))
        {
            builder.Configuration["ConnectionStrings:DefaultConnection"] = dbConnectionStringEnv;
        }

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

                if (missingField.EndsWith("parameter or property was not provided."))
                {
                    // Example strings:
                    // A value for the 'limit' parameter or property was not provided.
                    // A value for the 'page' parameter or property was not provided.
                    missingField = missingField.Split(" ")[4];
                    missingField = missingField.Substring(1, missingField.Length - 2);
                }

                return ApiResponse.setResponse("PARAMETER_IS_MANDATORY", new { }, string.Join(", ", missingField));
            };
        });

        builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
            {
                builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        return builder;
    }

    public static void AddAuthToSwagger(WebApplicationBuilder builder, string key)
    {
        builder.Services.AddSwaggerGen(opt =>
        {
            opt.SupportNonNullableReferenceTypes();
            opt.AddSecurityDefinition(key, new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Please enter " + key,
                Name = key,
                Type = SecuritySchemeType.ApiKey
            });
            opt.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type=ReferenceType.SecurityScheme,
                            Id=key
                        }
                    },
                    new string[]{}
                }
            });
        });
    }

    public static void EnableRedis(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<Redis>();
    }

    public static void ValidateEnvs(params string[] requiredVars)
    {
        var missingVars = requiredVars
            .Where(var => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(var)))
            .ToList();

        if (missingVars.Count > 0)
        {
            throw new Exception($"The following environment variables are not defined: {string.Join(", ", missingVars)}");
        }
    }

    private static string GetSubdirectoryPath(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri != null)
        {
            return string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/" ? "" : uri.AbsolutePath;
        }

        return "";
    }

    public static WebApplication InitApp(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            ValidateEnvs("API_URL");

            string apiURL = Environment.GetEnvironmentVariable("API_URL")!;

            app.UseSwagger(c =>
            {
                c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
                {
                    swaggerDoc.Servers = [
                        new OpenApiServer {
                            Url = apiURL
                        }
                    ];
                });
            });
            app.UseSwaggerUI();
            app.MapScalarApiReference(options => {
                options.OpenApiRoutePattern = GetSubdirectoryPath(apiURL) + "/swagger/{documentName}/swagger.json";
                options.EndpointPathPrefix = "/scalar/{documentName}";
            });
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
            ValidateEnvs("SERVER_WEBSOCKET_HTTP_PORT", "WEBSOCKET_URL");
        }

        if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "AWS")
        {
            ValidateEnvs("AWS_WEBSOCKET_ENDPOINT", "AWS_REGION", "WEBSOCKET_URL");
        }

        if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "AZURE")
        {
            ValidateEnvs("AZURE_WEBSOCKET_ENDPOINT", "AZURE_WEBSOCKET_ACCESS_TOKEN", "AZURE_WEBSOCKET_HUB");
        }

        return app;
    }
}

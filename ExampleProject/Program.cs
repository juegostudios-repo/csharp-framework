using API.Library;
using JuegoFramework.Helpers;

Application.InitLogger();

var builder = Application.InitBuilder(args);
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<IWebSocketHandler, WebSocketHandler>();

var app = Application.InitApp(builder);

app.UseMiddleware<ApiLoggingMiddleware>(Array.Empty<object>());

await Application.InitCron();

app.MapGet("/", () => Results.Text("API is running!"));

var azureFunctionCustomHandlerPort = Environment.GetEnvironmentVariable("FUNCTIONS_CUSTOMHANDLER_PORT");

if (azureFunctionCustomHandlerPort != null)
{
    app.UsePathBase("/api");
    app.Run($"http://+:{azureFunctionCustomHandlerPort}");
    Environment.Exit(0);
}

app.Run();

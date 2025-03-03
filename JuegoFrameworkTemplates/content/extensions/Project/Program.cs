using ProjectName.Library;
using JuegoFramework.Helpers;
using dotenv.net;

//-:cnd:noEmit
#if DEBUG
DotEnv.Load();
#endif
//+:cnd:noEmit

Application.InitLogger();

var builder = Application.InitBuilder(args);
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<IWebSocketHandler, WebSocketHandler>();
Application.AddAuthToSwagger(builder, "access_token");

var app = Application.InitApp(builder);

app.UseMiddleware<ApiLoggingMiddleware>();

app.UseCors("AllowAll");

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

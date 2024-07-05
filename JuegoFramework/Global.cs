using JuegoFramework.Helpers;

public static class Global
{
    public static IServiceProvider? ServiceProvider { get; set; }
    public static IEnumerable<EndpointDataSource>? EndpointSources { get; set; }
    public static IConfiguration? Configuration { get; set; }
    public static Dictionary<string, ResponseJson>? BaseResponse { get; set; }
    public static IHostEnvironment? Environment { get; set; }
    public static string? ConnectionString { get; set; }
}

namespace JuegoFramework.Helpers
{
    public class WebSocketMiddleware(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path == "/")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var scope = context.RequestServices.CreateScope();
                    var webSocketService = scope.ServiceProvider.GetRequiredService<WebSocketService>();
                    await webSocketService.HandleWebSocketAsync(context);
                } else {
                    await _next(context);
                }
            }
            else
            {
                await _next(context);
            }
        }
    }
}

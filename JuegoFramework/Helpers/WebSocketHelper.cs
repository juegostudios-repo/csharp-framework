using System.Text.Json;
using JuegoFramework.Controllers.Dto;

namespace JuegoFramework.Helpers
{
    public class WebSocketHelper
    {
        public static async Task HandleSocketMessage(string message)
        {
            Guid requestId = Guid.NewGuid();
            Log.Information($"{requestId} Received Message: {message}");
            try
            {
                var messageData = JsonSerializer.Deserialize<SocketEventDto>(message) ?? throw new ArgumentNullException(nameof(message), "The Message is not set.");
                var result = await RouteExecutor.Execute(messageData.Method, messageData.Action, messageData.Body.ToString(), messageData.Headers.AccessToken);
                Log.Information($"{requestId} Result: {JsonSerializer.Serialize(result.Value)}");
            }
            catch (Exception e)
            {
                Log.Information("Error: " + e.Message);
                Log.Information("Trace: " + e.StackTrace);
            }
        }
    }
}

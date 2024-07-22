using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace JuegoFramework.Helpers
{
    class APIWebSocket
    {
        private static readonly string serverWebsocketHttpPort = Environment.GetEnvironmentVariable("SERVER_WEBSOCKET_HTTP_PORT") ?? "";

        public static async Task SendMessageAsync(string connectionId, byte[] buffer)
        {
            Guid requestId = Guid.NewGuid();

            if (string.IsNullOrEmpty(connectionId))
            {
                Log.Information($"{requestId}: APIWebSocket.SendMessageAsync: Empty connectionId given");
                return;
            }

            var watch = Stopwatch.StartNew();

            Log.Information($"{requestId}: APIWebSocket.SendMessageAsync: Sending message to connection: {connectionId}");

            using HttpClient client = new();
            var uri = new Uri($"http://localhost:{serverWebsocketHttpPort}/api-websocket");

            try
            {
                string message = Encoding.UTF8.GetString(buffer);
                var jsonString = JsonSerializer.Serialize(new { connection_id = connectionId, message = message });
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(uri, content);

                if (response.IsSuccessStatusCode)
                {
                    Log.Information($"{requestId}: APIWebSocket.SendMessageAsync: Message sent successfully!");
                }
                else
                {
                    Log.Information($"{requestId}: APIWebSocket.SendMessageAsync: Error sending message: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                Log.Information($"{requestId}: APIWebSocket.SendMessageAsync: Error sending message: {ex.Message}");
            }
            finally
            {
                watch.Stop();
                Log.Information($"{requestId}: APIWebSocket.SendMessageAsync: Time to send message: {watch.ElapsedMilliseconds}ms");
            }
        }
    }
}

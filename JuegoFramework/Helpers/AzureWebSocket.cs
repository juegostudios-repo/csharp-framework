using Azure;
using Azure.Messaging.WebPubSub;
using System.Text;

namespace JuegoFramework.Helpers
{
    class AzureWebSocket
    {
        private static readonly string ApiGatewayEndpoint = Environment.GetEnvironmentVariable("AZURE_WEBSOCKET_ENDPOINT") ?? "";
        private static readonly string AccessToken = Environment.GetEnvironmentVariable("AZURE_WEBSOCKET_ACCESS_TOKEN") ?? "";
        private static readonly string Hub = Environment.GetEnvironmentVariable("AZURE_WEBSOCKET_HUB") ?? "";

        public static string CreateWebSocketUrl(string userId)
        {
            var webPubSubServiceClient = new WebPubSubServiceClient(new Uri(ApiGatewayEndpoint), Hub, new AzureKeyCredential(AccessToken));

            Uri clientAccessUri = webPubSubServiceClient.GetClientAccessUri(expiresAt: DateTimeOffset.MaxValue, userId: userId);

            return clientAccessUri.ToString();
        }

        public static async Task SendMessageAsync(string connectionId, byte[] buffer)
        {
            Guid requestId = Guid.NewGuid();

            if (string.IsNullOrEmpty(connectionId))
            {
                Log.Information($"{requestId}: AzureWebSocket.SendMessageAsync: Empty connectionId given");
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();

            Log.Information($"{requestId}: AzureWebSocket.SendMessageAsync: Sending message to connection: {connectionId}");

            var webPubSubServiceClient = new WebPubSubServiceClient(new Uri(ApiGatewayEndpoint), Hub, new AzureKeyCredential(AccessToken));

            try
            {
                string message = Encoding.UTF8.GetString(buffer);
                await webPubSubServiceClient.SendToConnectionAsync(connectionId, message);
                Log.Information($"{requestId}: AzureWebSocket.SendMessageAsync: Message sent successfully!");
            }
            catch (Exception ex)
            {
                Log.Information($"{requestId}: AzureWebSocket.SendMessageAsync: Error sending message: {ex.Message}");
            }
            finally
            {
                watch.Stop();
                Log.Information($"{requestId}: AzureWebSocket.SendMessageAsync: Time to send message: {watch.ElapsedMilliseconds}ms");
            }
        }
    }
}

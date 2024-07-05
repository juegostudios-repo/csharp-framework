using Amazon;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;

namespace JuegoFramework.Helpers
{
    class AWSWebSocket
    {
        private static readonly string ApiGatewayEndpoint = Environment.GetEnvironmentVariable("AWS_WEBSOCKET_ENDPOINT") ?? "http://localhost:3001";
        private static readonly string AuthenticationRegion = RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("AWS_REGION")).SystemName;

        public static async Task SendMessageAsync(string connectionId, byte[] buffer)
        {
            Guid requestId = Guid.NewGuid();

            if (string.IsNullOrEmpty(connectionId))
            {
                Log.Information($"{requestId}: AWSWebSocket.SendMessageAsync: Empty connectionId given");
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();

            Log.Information($"{requestId}: AWSWebSocket.SendMessageAsync: Sending message to connection: {connectionId}");

            var config = new AmazonApiGatewayManagementApiConfig
            {
                ServiceURL = ApiGatewayEndpoint,
                AuthenticationRegion = AuthenticationRegion
            };

            using var client = new AmazonApiGatewayManagementApiClient(config);
            var postRequest = new PostToConnectionRequest
            {
                ConnectionId = connectionId,
                Data = new MemoryStream(buffer)
            };

            try
            {
                await client.PostToConnectionAsync(postRequest);
                Log.Information($"{requestId}: AWSWebSocket.SendMessageAsync: Message sent successfully!");
            }
            catch (Exception ex)
            {
                Log.Information($"{requestId}: AWSWebSocket.SendMessageAsync: Error sending message: {ex.Message}");
            }
            finally
            {
                watch.Stop();
                Log.Information($"{requestId}: AWSWebSocket.SendMessageAsync: Time to send message: {watch.ElapsedMilliseconds}ms");
            }
        }
    }
}

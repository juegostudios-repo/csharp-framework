using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace JuegoFramework.Helpers
{
    public class WebSocketService
    {
        private static readonly Dictionary<string, WebSocket> SocketConnections = new();

        public async Task HandleWebSocketAsync(HttpContext context)
        {
            var webSocketHandlerService = context.RequestServices.GetService(typeof(IWebSocketHandler)) as IWebSocketHandler ?? throw new InvalidOperationException("Unable to find websocket handler service.");

            // Handle the WebSocket connection...
            WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
            string connectionId = context.Connection.Id;
            SocketConnections[connectionId] = webSocket;

            string token = context.Request.Query["access_token"].ToString() ?? string.Empty;
            Log.Information("token: {0}", token);

            await webSocketHandlerService.ConnectSocket(token, connectionId);

            // Add a buffer for receiving data.
            var buffer = new byte[1024 * 4];

            // Continuously receive data until the client closes the connection.
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    await WebSocketHelper.HandleSocketMessage(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    // If the client sent a close message, then close the connection.
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (webSocket.State == WebSocketState.Open)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by the server", CancellationToken.None);
                            if (SocketConnections.Remove(connectionId))
                            {
                                await webSocketHandlerService.DisconnectSocket(connectionId);
                            }
                            break;
                        }
                    }
                }
            }
            catch (WebSocketException e) when (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                // The client disconnected.
                // Handle unexpected client disconnection
                if (webSocket.State != WebSocketState.Closed)
                {
                    // If the WebSocket is not already closed, close it.
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    if (SocketConnections.Remove(connectionId))
                    {
                        await webSocketHandlerService.DisconnectSocket(connectionId);
                    }
                }
            }
            finally
            {
                if (webSocket.State != WebSocketState.Closed)
                {
                    // If the WebSocket is not already closed, close it.
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    if (SocketConnections.Remove(connectionId))
                    {
                        await webSocketHandlerService.DisconnectSocket(connectionId);
                    }
                }
            }
        }

        public static async Task SendMessageToSocket(string connectionId, object message)
        {
            var messageJson = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(messageJson);

            if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") != "AWS" && Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") != "AZURE")
            {
                if (SocketConnections.TryGetValue(connectionId, out var socket))
                {
                    if (socket.State != WebSocketState.Open)
                    {
                        return;
                    }

                    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }

            if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "AWS")
            {
                await AWSWebSocket.SendMessageAsync(connectionId, buffer);
            }

            if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "AZURE")
            {
                await AzureWebSocket.SendMessageAsync(connectionId, buffer);
            }
        }

        public bool TryGetSocket(string connectionId, out WebSocket? socket)
        {
            return SocketConnections.TryGetValue(connectionId, out socket);
        }

        public static string CreateWebSocketUrl(string userId)
        {
            if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "AZURE")
            {
                return AzureWebSocket.CreateWebSocketUrl(userId);
            }

            return string.Empty;
        }
    }
}

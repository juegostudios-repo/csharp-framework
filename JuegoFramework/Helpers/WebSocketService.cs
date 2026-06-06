using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace JuegoFramework.Helpers
{
    public class WebSocketService
    {
        private static readonly ConcurrentDictionary<string, WebSocket> SocketConnections = new();

        // Serializes concurrent sends to one socket. Both the receive loop (responses,
        // pongs) and the backplane subscriber can target the same connection, and a
        // WebSocket forbids overlapping SendAsync calls.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> SendLocks = new();

        public async Task HandleWebSocketAsync(HttpContext context)
        {
            var webSocketHandlerService = context.RequestServices.GetService(typeof(IWebSocketHandler)) as IWebSocketHandler ?? throw new InvalidOperationException("Unable to find websocket handler service.");

            // Handle the WebSocket connection...
            WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
            string connectionId = Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "SERVER_CLUSTER"
                ? WebSocketBackplane.NewConnectionId()
                : context.Connection.Id;
            SocketConnections.TryAdd(connectionId, webSocket);
            SendLocks.TryAdd(connectionId, new SemaphoreSlim(1, 1));

            string token = context.Request.Query["access_token"].ToString() ?? string.Empty;
            Log.Information("token: {0}", token);

            try
            {
                await webSocketHandlerService.ConnectSocket(token, connectionId);
            }
            catch (Exception ex)
            {
                // Connect-time registration/auth failed: reject the connection cleanly with a
                // close frame instead of letting the exception abort it. The client gets a
                // clean close and can re-login/reconnect rather than holding a half-open socket.
                Log.Warning(ex, "ConnectSocket failed for {ConnectionId}; rejecting connection", connectionId);
                SocketConnections.TryRemove(connectionId, out _);
                SendLocks.TryRemove(connectionId, out _);
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Authentication failed", CancellationToken.None);
                }
                return;
            }

            // Add a buffer for receiving data.
            var buffer = new byte[1024 * 4];

            // Continuously receive data until the client closes the connection.
            try
            {
                // Idle deadline (0 disables): close a socket that sends nothing for this long.
                // An explicit env value always wins. When unset, cluster mode defaults to 90s so
                // the zombie-connection cleanup isn't silently off, while SERVER stays disabled to
                // preserve its original behaviour.
                int idleTimeoutSeconds;
                if (int.TryParse(Environment.GetEnvironmentVariable("WEBSOCKET_IDLE_TIMEOUT_SECONDS"), out var s) && s >= 0)
                {
                    idleTimeoutSeconds = s;
                }
                else
                {
                    idleTimeoutSeconds = Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "SERVER_CLUSTER" ? 90 : 0;
                }
                while (webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    if (idleTimeoutSeconds > 0)
                    {
                        using var idleCts = new CancellationTokenSource(TimeSpan.FromSeconds(idleTimeoutSeconds));
                        try
                        {
                            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), idleCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // No frame within the deadline: treat as dead. The cancellation has
                            // aborted the socket, so we skip CloseAsync and let the finally block
                            // run DisconnectSocket via TryRemove.
                            Log.Information("WebSocket {ConnectionId} idle-timed out after {IdleSeconds}s", connectionId, idleTimeoutSeconds);
                            break;
                        }
                    }
                    else
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    }

                    var messageText = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    if (WebSocketBackplane.IsPingMessage(messageText))
                    {
                        // Reset is implicit: the next loop iteration starts a fresh deadline. Reply
                        // pong inline so the client can detect a dead connection. No routing.
                        await DeliverLocalAsync(connectionId, Encoding.UTF8.GetBytes(WebSocketBackplane.PongMessage));
                    }
                    else
                    {
                        var response = await WebSocketHelper.HandleSocketMessage(messageText);
                        if (response != null)
                        {
                            await SendMessageToSocket(connectionId, response);
                        }
                    }

                    // If the client sent a close message, then close the connection.
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (webSocket.State == WebSocketState.Open)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by the server", CancellationToken.None);
                            if (SocketConnections.TryRemove(connectionId, out _))
                            {
                                SendLocks.TryRemove(connectionId, out _);
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
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived || webSocket.State == WebSocketState.CloseSent)
                {
                    // If the WebSocket is not already closed, close it.
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                if (SocketConnections.TryRemove(connectionId, out _))
                {
                    SendLocks.TryRemove(connectionId, out _);
                    await webSocketHandlerService.DisconnectSocket(connectionId);
                }
            }
            finally
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived || webSocket.State == WebSocketState.CloseSent)
                {
                    // If the WebSocket is not already closed, close it.
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                if (SocketConnections.TryRemove(connectionId, out _))
                {
                    SendLocks.TryRemove(connectionId, out _);
                    await webSocketHandlerService.DisconnectSocket(connectionId);
                }
            }
        }

        // Sends to a socket this instance holds, serialized per connection. No-op if the
        // connection is not local or not open.
        private static async Task DeliverLocalAsync(string connectionId, byte[] buffer)
        {
            if (!SocketConnections.TryGetValue(connectionId, out var socket) || socket.State != WebSocketState.Open)
            {
                return;
            }

            var gate = SendLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                // Best-effort delivery: a socket that faults mid-send must not propagate and
                // stall a fan-out to the rest of the room.
                Log.Warning(ex, "DeliverLocalAsync: send failed for {ConnectionId}", connectionId);
            }
            finally
            {
                gate.Release();
            }
        }

        // Entry point the backplane subscriber calls when a message arrives over Redis.
        public static async Task DeliverFromBackplane(WebSocketBackplane.Envelope env)
        {
            var buffer = Encoding.UTF8.GetBytes(env.Message);
            if (env.ConnectionId != null)
            {
                await DeliverLocalAsync(env.ConnectionId, buffer);
            }
            if (env.ConnectionIds != null)
            {
                foreach (var cid in env.ConnectionIds)
                {
                    await DeliverLocalAsync(cid, buffer);
                }
            }
        }

        public static async Task SendMessageToSocket(string connectionId, object message, bool skipJsonSerialization = false)
        {
            var messageJson = !skipJsonSerialization ? JsonSerializer.Serialize(message) : (string)message;
            var buffer = Encoding.UTF8.GetBytes(messageJson);
            var wsSystem = Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM");

            if (wsSystem == "SERVER")
            {
                if (Environment.GetEnvironmentVariable("MODE") == "CRON")
                {
                    await APIWebSocket.SendMessageAsync(connectionId, buffer);
                    return;
                }

                if (SocketConnections.TryGetValue(connectionId, out var socket))
                {
                    if (socket.State != WebSocketState.Open)
                    {
                        return;
                    }

                    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }

            if (wsSystem == "SERVER_CLUSTER")
            {
                if (WebSocketBackplane.IsForThisInstance(connectionId))
                {
                    await DeliverLocalAsync(connectionId, buffer);
                }
                else if (WebSocketBackplane.TryGetInstanceId(connectionId, out var instanceId))
                {
                    await WebSocketBackplane.PublishAsync(instanceId, new WebSocketBackplane.Envelope
                    {
                        ConnectionId = connectionId,
                        Message = messageJson
                    });
                }
                else
                {
                    Log.Warning("SendMessageToSocket: unroutable connectionId {ConnectionId}", connectionId);
                }
            }

            if (wsSystem == "AWS")
            {
                await AWSWebSocket.SendMessageAsync(connectionId, buffer);
            }

            if (wsSystem == "AZURE")
            {
                await AzureWebSocket.SendMessageAsync(connectionId, buffer);
            }
        }

        // Batched fan-out. In SERVER_CLUSTER, recipients are grouped by owning instance
        // and each remote instance gets ONE publish carrying its connection-id list,
        // instead of one publish per recipient. Other modes fall back to per-connection.
        public static async Task SendMessageToSockets(IEnumerable<string> connectionIds, object message, bool skipJsonSerialization = false)
        {
            var messageJson = !skipJsonSerialization ? JsonSerializer.Serialize(message) : (string)message;

            if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") != "SERVER_CLUSTER")
            {
                foreach (var cid in connectionIds)
                {
                    // Isolate each send: one dead/stale connection must not stall or
                    // abort delivery to the rest of the recipients.
                    try
                    {
                        await SendMessageToSocket(cid, messageJson, skipJsonSerialization: true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "SendMessageToSockets: send failed for {ConnectionId}", cid);
                    }
                }
                return;
            }

            var buffer = Encoding.UTF8.GetBytes(messageJson);
            var tasks = new List<Task>();
            foreach (var (instanceId, cids) in WebSocketBackplane.GroupByInstance(connectionIds))
            {
                if (instanceId == WebSocketBackplane.InstanceId)
                {
                    foreach (var cid in cids)
                    {
                        tasks.Add(DeliverLocalAsync(cid, buffer));
                    }
                }
                else
                {
                    tasks.Add(WebSocketBackplane.PublishAsync(instanceId, new WebSocketBackplane.Envelope
                    {
                        ConnectionIds = cids,
                        Message = messageJson
                    }));
                }
            }
            await Task.WhenAll(tasks);
        }

        // Closes every socket this instance holds and clears its DB mapping. Called on
        // ApplicationStopping so a scale-in does not leave dangling connection_id rows.
        public static async Task DrainAllAsync()
        {
            foreach (var connectionId in SocketConnections.Keys.ToArray())
            {
                if (SocketConnections.TryGetValue(connectionId, out var socket))
                {
                    try
                    {
                        if (socket.State == WebSocketState.Open)
                        {
                            // CloseOutputAsync sends the close frame without waiting for the
                            // client's ACK. During scale-in the clients being drained are often
                            // gone/slow, and CloseAsync would block on each missing ACK and stall
                            // the drain past the shutdown timeout. Best-effort notify is enough;
                            // the DB mapping is cleared below regardless.
                            await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "server draining", CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "DrainAllAsync: close failed for {ConnectionId}", connectionId);
                    }
                }

                if (SocketConnections.TryRemove(connectionId, out _))
                {
                    // Remove without Dispose: we never touch AvailableWaitHandle, so the
                    // SemaphoreSlim holds no unmanaged resource and GC reclaims it. Disposing
                    // here would race a concurrent DeliverLocalAsync that already holds the
                    // reference (ObjectDisposedException on WaitAsync).
                    SendLocks.TryRemove(connectionId, out _);
                    try
                    {
                        using var scope = Global.ServiceProvider!.CreateScope();
                        var handler = scope.ServiceProvider.GetRequiredService<IWebSocketHandler>();
                        await handler.DisconnectSocket(connectionId);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "DrainAllAsync: DisconnectSocket failed for {ConnectionId}", connectionId);
                    }
                }
            }
        }

        public bool TryGetSocket(string connectionId, out WebSocket? socket)
        {
            return SocketConnections.TryGetValue(connectionId, out socket);
        }

        public static string CreateWebSocketUrl(string accessToken)
        {
            var wsSystem = Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM");
            if (wsSystem == "SERVER" || wsSystem == "SERVER_CLUSTER" || wsSystem == "AWS")
            {
                return Environment.GetEnvironmentVariable("WEBSOCKET_URL") + "?access_token=" + accessToken;
            }

            if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") == "AZURE")
            {
                return AzureWebSocket.CreateWebSocketUrl(accessToken);
            }

            return string.Empty;
        }
    }
}

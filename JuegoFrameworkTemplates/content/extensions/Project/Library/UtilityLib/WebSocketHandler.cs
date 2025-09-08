using System.Text.Json;
using ProjectName.Models;
using JuegoFramework.Helpers;

namespace ProjectName.Library
{
    public class WebSocketHandler : IWebSocketHandler
    {
#if DbTypeMySql
        private const bool _enableApiLogging = true;

#endif
        public override async Task ConnectSocket(string accessToken, string connectionId)
        {
#if DbTypeMySql
            if (_enableApiLogging)
            {
                var user = await UserLib.FindOne(new
                {
                    connection_id = connectionId
                });

                await ApiLogLib.Insert(new ApiLog
                {
                    UserId = user?.UserId,
                    Method = "WebSocket",
                    Path = "ConnectSocket",
                    Request = $"{connectionId} Connected",
                    Response = "",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await UserLib.ConnectSocket(accessToken, connectionId);
#endif
        }

        public override async Task DisconnectSocket(string connectionId)
        {
#if DbTypeMySql
            if (_enableApiLogging)
            {
                var user = await UserLib.FindOne(new
                {
                    connection_id = connectionId
                });

                await ApiLogLib.Insert(new ApiLog
                {
                    UserId = user?.UserId,
                    Method = "WebSocket",
                    Path = "DisconnectSocket",
                    Request = $"{connectionId} Disconnected",
                    Response = "",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await UserLib.DisconnectSocket(connectionId);
#endif
        }

        public static async Task SendMessageToSocket(string connectionId, object message)
        {
#if DbTypeMySql
            if (_enableApiLogging)
            {
                var user = await UserLib.FindOne(new
                {
                    connection_id = connectionId
                });

                string messageString = JsonSerializer.Serialize(message);

                await ApiLogLib.Insert(new ApiLog
                {
                    UserId = user?.UserId,
                    Method = "WebSocket",
                    Path = "SendMessageToSocket",
                    Request = $"Message to {connectionId}",
                    Response = messageString,
                    CreatedAt = DateTime.UtcNow
                });
            }

#endif
            await WebSocketService.SendMessageToSocket(connectionId, message);
        }
    }
}

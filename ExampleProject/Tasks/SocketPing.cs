using API.Globals;
using API.Library;
using JuegoFramework.Helpers;

namespace API.Tasks
{
    public class SocketPing : Cron
    {
        public override TimeSpan Interval => TimeSpan.FromSeconds(Constants.CRON_TIMER.SOCKET_PING_TIME);

        public override async Task<Task> Run()
        {
            try
            {
                Log.Information("=====SocketPing=====");

                var socketUsers = await UserLib.GetWebsocketConnectedUsersList();
                for (int i = 0; i < socketUsers.Count; i++)
                {
                    _ = WebSocketHandler.SendMessageToSocket(socketUsers[i].ConnectionId ?? "", new
                    {
                        eventCode = Constants.SOCKET_EVENT.CODE.SOCKET_PING,
                        eventMessage = Constants.SOCKET_EVENT.MESSAGE.SOCKET_PING,
                        eventData = new
                        {
                            socket_id = socketUsers[i].ConnectionId ?? ""
                        }
                    });
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "SocketPing Error");
            }
            return Task.CompletedTask;
        }
    }
}

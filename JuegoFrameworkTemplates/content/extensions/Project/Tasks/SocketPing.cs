using ProjectName.Globals;
using ProjectName.Library;
using JuegoFramework.Helpers;

namespace ProjectName.Tasks
{
    public class SocketPing : Cron
    {
        public override TimeSpan Interval => TimeSpan.FromSeconds(Constants.CRON_TIMER.SOCKET_PING_TIME);

        // Uncomment the line below if you want to use a cron expression instead of an interval and remove the Interval property.
        // public override string Expression => "*/10 * * * * *";

        private readonly Serilog.ILogger _logger;
        public SocketPing()
        {
            _logger = Log.ForContext("CronName", GetType().Name);
        }

        public override async Task Run()
        {
            try
            {
                _logger.Information("=====SocketPing=====");

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
                _logger.Error(e, "SocketPing Error");
            }
        }
    }
}

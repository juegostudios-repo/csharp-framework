namespace JuegoFramework.Helpers
{
    public abstract class IWebSocketHandler
    {
        public abstract Task ConnectSocket(string accessToken, string connectionId);
        public abstract Task DisconnectSocket(string connectionId);
    }
}

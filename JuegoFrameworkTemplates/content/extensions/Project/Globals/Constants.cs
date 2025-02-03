namespace ProjectName.Globals;

public static class Constants
{
    public static class STATUS
    {
        public const int PENDING = 1;
        public const int ACTIVE = 2;
        public const int LEFT = 3;
        public const int COMPLETED = 4;
        public const int DELETED = 5;
        public const int INACTIVE = 6;
    }

    public static class SOCKET_EVENT
    {
        public static class CODE
        {
            public const int SOCKET_PING = 1;
        }

        public static class MESSAGE
        {
            public const string SOCKET_PING = "socket_ping";
        }
    }

    public static class CRON_TIMER
    {
        public const int SOCKET_PING_TIME = 6 * 60;  //Every 6 min

    }
}

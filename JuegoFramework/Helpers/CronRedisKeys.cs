namespace JuegoFramework.Helpers
{
    /// <summary>
    /// Every Redis key the cron machinery writes, in one place. Redis.AcquireLockAsync does not
    /// apply REDIS_PREFIX_KEY, so the cron side prefixes its own keys and nothing else has to know
    /// the layout.
    /// </summary>
    internal static class CronRedisKeys
    {
        private static readonly string _redisPrefixKey = Environment.GetEnvironmentVariable("REDIS_PREFIX_KEY") ?? "";

        /// <summary>
        /// True when REDIS_CONNECTION_STRING is set. Read on every call, because the tests flip it.
        /// </summary>
        internal static bool IsConfigured => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"));

        /// <summary>
        /// The root every cron key hangs off: "{REDIS_PREFIX_KEY}:cron", or just "cron" when no
        /// prefix is configured.
        /// </summary>
        internal static string Root => string.IsNullOrEmpty(_redisPrefixKey) ? "cron" : $"{_redisPrefixKey}:cron";

        /// <summary>
        /// The process wide wake channel: "{prefix}:cron:wake".
        /// </summary>
        internal static string WakeChannel => $"{Root}:wake";

        /// <summary>
        /// The slot claim for one clock aligned tick: "{prefix}:cron:{JobName}:slot:{tickUnixMs}".
        /// Milliseconds, so an interval under a second still gets one slot per tick.
        /// </summary>
        internal static string Slot(string jobName, long tickUnixMilliseconds) => $"{Root}:{jobName}:slot:{tickUnixMilliseconds}";

        /// <summary>
        /// The running lock held while a single runner job's Run is in flight:
        /// "{prefix}:cron:{JobName}:running".
        /// </summary>
        internal static string Running(string jobName) => $"{Root}:{jobName}:running";

        /// <summary>
        /// The claim on one fan out item: "{prefix}:cron:{JobName}:item:{ItemKey}".
        /// </summary>
        internal static string Item(string jobName, string itemKey) => $"{Root}:{jobName}:item:{itemKey}";

        /// <summary>
        /// The set of every job name a worker has started: "{prefix}:cron:jobs". Lets a reader list
        /// the jobs without scanning the keyspace.
        /// </summary>
        internal static string Jobs => $"{Root}:jobs";

        /// <summary>
        /// The hash with a job's shape and its latest run: "{prefix}:cron:{JobName}:status".
        /// Written and read by <see cref="CronStatus"/>.
        /// </summary>
        internal static string Status(string jobName) => $"{Root}:{jobName}:status";

        /// <summary>
        /// The stream of a job's past runs, newest last: "{prefix}:cron:{JobName}:runs". Capped at
        /// <see cref="CronStatus.RUN_HISTORY_LENGTH"/> entries.
        /// </summary>
        internal static string Runs(string jobName) => $"{Root}:{jobName}:runs";
    }
}

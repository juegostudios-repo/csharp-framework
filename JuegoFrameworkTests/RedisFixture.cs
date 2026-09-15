using JuegoFramework.Helpers;
using StackExchange.Redis;

namespace JuegoFrameworkTests
{
    /// <summary>
    /// Shared state for the Redis backed tests. Reads REDIS_CONNECTION_STRING and, when it is set,
    /// hands out the framework's own connection so that the tests see exactly the keys the cron
    /// machinery writes. Nothing here connects until a test asks for the database, so a run without
    /// Redis configured is free.
    /// </summary>
    public sealed class RedisFixture
    {
        /// <summary>
        /// True when REDIS_CONNECTION_STRING is set, which is what decides whether the Redis backed
        /// tests run or are skipped.
        /// </summary>
        public static bool IsRedisConfigured => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"));

        /// <summary>
        /// The message a skipped Redis test reports.
        /// </summary>
        public const string SKIP_REASON = "REDIS_CONNECTION_STRING is not set, skipping the Redis backed tests";

        /// <summary>
        /// The database the framework itself uses.
        /// </summary>
        public IDatabase Database => Redis.Database;

        /// <summary>
        /// Deletes every cron key, so one test cannot see a claim another test left behind.
        /// </summary>
        public async Task ResetCronKeysAsync()
        {
            var connection = Redis.Redis2;

            foreach (var endpoint in connection.GetEndPoints())
            {
                var server = connection.GetServer(endpoint);

                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                foreach (var key in server.Keys(pattern: $"{CronRedisKeys.Root}:*"))
                {
                    await Database.KeyDeleteAsync(key);
                }
            }
        }
    }

    /// <summary>
    /// A Fact that skips itself when Redis is not configured. xUnit 2.9.3 has no Skip.If, so the
    /// decision is made when the attribute is built.
    /// </summary>
    public sealed class RedisFactAttribute : FactAttribute
    {
        /// <summary>
        /// Marks the test skipped unless REDIS_CONNECTION_STRING is set.
        /// </summary>
        public RedisFactAttribute()
        {
            if (!RedisFixture.IsRedisConfigured)
            {
                Skip = RedisFixture.SKIP_REASON;
            }
        }
    }

    /// <summary>
    /// The cron tests share process wide state: the CronJobService statics and the CronWake handler
    /// table. Putting them in one non parallel collection keeps one test's jobs out of another's.
    /// </summary>
    [CollectionDefinition(NAME, DisableParallelization = true)]
    public sealed class CronCollection : ICollectionFixture<RedisFixture>
    {
        /// <summary>
        /// The collection name the cron test classes carry.
        /// </summary>
        public const string NAME = "Cron";
    }
}

using System.Collections.Concurrent;
using StackExchange.Redis;

namespace JuegoFramework.Helpers
{
    /// <summary>
    /// The wake channel. A web instance that has just created work a cron job cares about publishes
    /// the job's type name here, and the worker running that job cuts its current sleep short
    /// instead of waiting out the rest of its interval.
    /// A wake is a hint, never a guarantee: it can be lost when no worker is listening, so a job
    /// must still make progress on its own schedule.
    /// </summary>
    public static class CronWake
    {
        private static readonly ConcurrentDictionary<string, Action> _handlers = new(StringComparer.Ordinal);
        private static readonly Lock _subscribeLock = new();
        private static bool _subscribed;

        /// <summary>
        /// Publishes a wake for the given job. The name is the job's type name, for example
        /// <c>nameof(MarchArrivals)</c>. Static and safe to call from a web instance; it touches
        /// Redis only, never the cron runner.
        /// </summary>
        /// <param name="jobName">The type name of the cron job to wake.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public static async Task PublishAsync(string jobName)
        {
            ArgumentException.ThrowIfNullOrEmpty(jobName);

            await Redis.Redis2.GetSubscriber().PublishAsync(RedisChannel.Literal(CronRedisKeys.WakeChannel), jobName);
        }

        /// <summary>
        /// Publishes a wake for the given fan out job. Prefer this over the string overload: the
        /// name comes from the type, so a typo cannot turn the wake into a no-op, and only a job
        /// that can actually be woken is accepted.
        /// </summary>
        /// <typeparam name="TJob">The fan out job to wake.</typeparam>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public static Task PublishAsync<TJob>() where TJob : IFanOutJob => PublishAsync(typeof(TJob).Name);

        /// <summary>
        /// Registers a started fan out job. The process wide subscription is created the first time
        /// a job registers, so a fleet with no fan out job never touches the channel.
        /// </summary>
        internal static void Register(string jobName, Action onWake)
        {
            _handlers[jobName] = onWake;
            EnsureSubscribed();
        }

        /// <summary>
        /// Drops every job registration. The subscription itself is left in place: it belongs to the
        /// connection, not to any one job, and a wake naming a job nobody has registered is ignored.
        /// Used when the set of started jobs is replaced.
        /// </summary>
        internal static void ClearHandlers()
        {
            _handlers.Clear();
        }

        private static void EnsureSubscribed()
        {
            lock (_subscribeLock)
            {
                if (_subscribed)
                {
                    return;
                }

                // Only reachable outside CronJobService.Start, which refuses to start a fan out job
                // without Redis. The startup warning about a missing Redis lives there.
                if (!CronRedisKeys.IsConfigured)
                {
                    return;
                }

                try
                {
                    Redis.Redis2.GetSubscriber().Subscribe(RedisChannel.Literal(CronRedisKeys.WakeChannel), OnWakeMessage);
                    _subscribed = true;

                    Log.Information("Cron wake channel {Channel} subscribed", CronRedisKeys.WakeChannel);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Cron wake channel {Channel} could not be subscribed. Jobs will only run on their own schedule", CronRedisKeys.WakeChannel);
                }
            }
        }

        private static void OnWakeMessage(RedisChannel channel, RedisValue message)
        {
            var jobName = message.IsNull ? "" : message.ToString();

            if (string.IsNullOrEmpty(jobName))
            {
                return;
            }

            if (!_handlers.TryGetValue(jobName, out var handler))
            {
                Log.Debug("Cron wake for {Name} ignored, no fan out job by that name runs in this process", jobName);
                return;
            }

            try
            {
                handler();
            }
            catch (Exception e)
            {
                // The handler only latches the job's wake signal, but a throw here would surface
                // on a StackExchange.Redis callback thread, where nothing would log it.
                Log.Error(e, "Cron wake for {Name} failed", jobName);
            }
        }
    }
}

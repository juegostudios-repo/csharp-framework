using System.Diagnostics;
using System.Threading.Channels;
using StackExchange.Redis;

namespace JuegoFramework.Helpers
{
    /// <summary>
    /// The single scheduling loop shared by <see cref="Cron"/> and <see cref="ScheduledCron"/>.
    /// The loop has three clear steps: compute the next tick, sleep until it, run the job.
    /// A failing run is logged and never breaks the loop.
    /// With Redis configured, a plain job claims each clock aligned slot so that one container in
    /// the fleet runs it. A fan out job is exempt: it ticks everywhere and splits the work through
    /// its own item claims, and its <c>NextDueIn</c> shortens the sleep and subscribes it to
    /// the wake channel.
    /// </summary>
    internal sealed class CronRunner
    {
        /// <summary>
        /// The floor on any sleep. Keeps a job whose NextDueIn keeps answering "now" from
        /// spinning, and keeps a flood of wakes from turning into a tight loop.
        /// </summary>
        internal static readonly TimeSpan MIN_SLEEP = TimeSpan.FromMilliseconds(250);

        private static readonly TimeSpan RUNNING_LOCK_EXPIRY = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RUNNING_LOCK_REFRESH = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan MIN_SLOT_EXPIRY = TimeSpan.FromSeconds(60);

        /// <summary>
        /// A NextDueIn slower than this is warned about: it runs before every sleep and after every
        /// wake on every container, so it must be an index lookup, not a scan.
        /// </summary>
        internal static readonly TimeSpan SLOW_NEXT_DUE_IN = TimeSpan.FromSeconds(1);

        // Compare and expire: the expiry is pushed out only while the key still holds this
        // instance's value, so a refresh cannot extend a lock another container has taken.
        private const string REFRESH_LOCK_SCRIPT = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";

        private readonly Func<CancellationToken, Task> _run;
        private readonly Func<DateTime, DateTime?> _nextTick;
        private readonly CancellationToken _stopping;
        private readonly Cron? _cron;
        private readonly bool _claimsSlot;
        private readonly Func<Task<TimeSpan?>>? _nextDueIn;
        private readonly IFanOutJob? _fanOut;
        private readonly Channel<bool> _wakeSignal = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private int _running;
        private DateTime _lastRunStartedUtc = DateTime.MinValue;

        /// <summary>
        /// Creates a runner for one job instance.
        /// </summary>
        /// <param name="job">The job instance, either a <see cref="Cron"/> or a <see cref="ScheduledCron"/>.</param>
        /// <param name="nextTick">Given the current UTC time, returns the UTC time of the next run, or null when the job has no further occurrence.</param>
        /// <param name="stopping">Cancelled when the process is shutting down. Cuts the sleep short; an in-flight run is left to finish.</param>
        /// <param name="redisConfigured">Whether REDIS_CONNECTION_STRING is set. With it, a plain job claims its ticks so one container runs each; without it, the job runs on its own timer.</param>
        internal CronRunner(object job, Func<DateTime, DateTime?> nextTick, CancellationToken stopping, bool redisConfigured)
        {
            ArgumentNullException.ThrowIfNull(job);

            _run = job switch
            {
                ScheduledCron scheduledCron => scheduledCron.Run,
                Cron cron => cron.Run,
                _ => throw new ArgumentException($"{job.GetType().Name} is neither a Cron nor a ScheduledCron", nameof(job))
            };
            _nextTick = nextTick ?? throw new ArgumentNullException(nameof(nextTick));
            _stopping = stopping;
            JobName = job.GetType().Name;
            _cron = job as Cron;

            if (job is IFanOutJob fanOut)
            {
                // A fan out job must tick on every container: its per item claims are what split
                // the work. It answers when its next work is due, and a wake cuts its sleep short.
                _nextDueIn = fanOut.NextDueIn;
                _fanOut = fanOut;
                CronWake.Register(JobName, SignalWake);
            }
            else
            {
                // A plain job runs once per tick across the fleet, which needs Redis to agree on
                // who. Without Redis there is one container, and its own timer is enough.
                _claimsSlot = redisConfigured;
            }
        }

        /// <summary>
        /// The name of the job this runner drives, used for logging and as the Redis key segment.
        /// </summary>
        internal string JobName { get; }

        /// <summary>
        /// True while the job's Run is in flight.
        /// </summary>
        internal bool IsRunning => Volatile.Read(ref _running) == 1;

        /// <summary>
        /// The clock aligned tick that follows <paramref name="nowUtc"/>, that is
        /// <c>floor(nowUnix / interval) * interval + interval</c>. Every container computes the same
        /// slots for the same interval, which is what makes a slot claim meaningful.
        /// </summary>
        internal static DateTime AlignedNextTick(DateTime nowUtc, TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "the interval must be positive");
            }

            var sinceEpoch = nowUtc.Ticks - DateTime.UnixEpoch.Ticks;
            var aligned = sinceEpoch / interval.Ticks * interval.Ticks + interval.Ticks;

            return new DateTime(DateTime.UnixEpoch.Ticks + aligned, DateTimeKind.Utc);
        }

        /// <summary>
        /// The sleep an interval job takes: <c>clamp(nextDueIn ?? interval, MIN_SLEEP, interval)</c>.
        /// A null answer means "nothing better than the interval is known", so the interval is used
        /// as is. The floor is applied last, so an interval below MIN_SLEEP still gets the floor
        /// once a job starts answering with a delay.
        /// </summary>
        internal static TimeSpan ClampSleep(TimeSpan? nextDueIn, TimeSpan interval)
        {
            if (nextDueIn is null)
            {
                return interval;
            }

            var delay = nextDueIn.Value;

            if (delay > interval)
            {
                delay = interval;
            }

            return delay < MIN_SLEEP ? MIN_SLEEP : delay;
        }

        /// <summary>
        /// Runs the schedule loop until the shutdown token is cancelled or the job has no next occurrence.
        /// The returned task completes only once any in-flight run has finished, so awaiting it drains the job.
        /// </summary>
        internal async Task RunLoopAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                // Guarantees the loop yields once per iteration, so a degenerate schedule can never
                // monopolise a thread-pool thread.
                await Task.Yield();

                var now = DateTime.UtcNow;
                DateTime? nextTick;
                var interval = TimeSpan.Zero;

                try
                {
                    if (_claimsSlot && _cron is not null)
                    {
                        interval = _cron.Interval;
                        nextTick = AlignedNextTick(now, interval);
                    }
                    else
                    {
                        nextTick = _nextTick(now);
                    }
                }
                catch (Exception e)
                {
                    // The job's own Interval or Expression is broken. Stop this loop the same way the
                    // no-next-occurrence case does, rather than faulting the task and dying silently.
                    Log.Error(e, "Cron {Name} failed to compute its next run, its loop is stopping", JobName);
                    return;
                }

                if (nextTick is null)
                {
                    Log.Error("Cron {Name} has no next occurrence, its loop is stopping", JobName);
                    return;
                }

                var delay = nextTick.Value - now;
                var wokenEarly = false;

                if (_nextDueIn is not null && delay > TimeSpan.Zero)
                {
                    if (_fanOut!.LostEveryClaim)
                    {
                        // Every item this container enumerated is in flight elsewhere or backing
                        // off after a failure. NextDueIn would answer "now" for those same items
                        // and re-enumerate them at the sleep floor for as long as they stay due,
                        // so keep the interval. New work still arrives as a wake.
                        Log.Debug("Cron {Name} won none of its items, sleeping its interval", JobName);
                    }
                    else
                    {
                        delay = ClampSleep(await ReadNextDueIn(), delay);
                    }
                }

                // Logged after the clamp: a nearer due time pulls the tick in ahead of the
                // schedule's own next occurrence, so the clamped delay is what actually fires.
                Log.Debug(
                    "Cron {Name} next run at {NextRun}",
                    JobName,
                    delay > TimeSpan.Zero ? now + delay : now);

                if (delay > TimeSpan.Zero)
                {
                    var slept = await SleepAsync(delay);

                    if (slept is null)
                    {
                        return;
                    }

                    wokenEarly = slept.Value;
                }

                if (_stopping.IsCancellationRequested)
                {
                    return;
                }

                // A wake cuts the sleep short, so back to back wakes could otherwise turn the loop
                // into a spin. The floor is measured from the last run, not from the sleep.
                if (wokenEarly && !await HonourMinSleepAsync())
                {
                    return;
                }

                if (_claimsSlot)
                {
                    await TryRunSlotAsync(nextTick.Value, interval);
                }
                else
                {
                    await RunOnceAsync();
                }
            }
        }

        /// <summary>
        /// Reads the job's NextDueIn. A job that throws here must not take its loop down, so
        /// the failure is logged and the sleep falls back to the interval.
        /// </summary>
        private async Task<TimeSpan?> ReadNextDueIn()
        {
            try
            {
                var started = Stopwatch.GetTimestamp();
                var nextDueIn = await _nextDueIn!();
                var took = Stopwatch.GetElapsedTime(started);

                if (took > SLOW_NEXT_DUE_IN)
                {
                    Log.Warning("Cron {Name} NextDueIn took {Took}. It runs before every sleep on every container, so index the query it makes", JobName, took);
                }

                return nextDueIn;
            }
            catch (Exception e)
            {
                Log.Error(e, "Cron {Name} NextDueIn failed, falling back to its interval", JobName);
                return null;
            }
        }

        /// <summary>
        /// Sleeps until the next tick, or until a wake arrives. Returns null when shutdown cut the
        /// sleep short, true when a wake did, false when the sleep ran its course.
        /// </summary>
        private async Task<bool?> SleepAsync(TimeSpan delay)
        {
            try
            {
                if (_nextDueIn is null)
                {
                    await Task.Delay(delay, _stopping);
                    return false;
                }

                // WaitToReadAsync only peeks, so the signal a sleep abandons when its delay wins is
                // still there for the next sleep to read. A waiter that hangs on a semaphore would
                // swallow it instead.
                // The loser of the race is cancelled once the winner is known: without that, every
                // sleep that ran its course would leave one more reader parked on the channel
                // until the next wake flushed them all, and a job that is seldom woken would
                // collect thousands of them.
                using var sleepSource = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
                var wake = _wakeSignal.Reader.WaitToReadAsync(sleepSource.Token).AsTask();
                var elapsed = Task.Delay(delay, sleepSource.Token);

                await Task.WhenAny(wake, elapsed);
                await sleepSource.CancelAsync();

                if (!_wakeSignal.Reader.TryRead(out _))
                {
                    return false;
                }

                Log.Debug("Cron {Name} was woken early", JobName);
                return true;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Holds the loop back until MIN_SLEEP has passed since the last run started. Returns false
        /// when shutdown cut the wait short.
        /// </summary>
        private async Task<bool> HonourMinSleepAsync()
        {
            var sinceLastRun = DateTime.UtcNow - _lastRunStartedUtc;

            if (sinceLastRun >= MIN_SLEEP)
            {
                return true;
            }

            try
            {
                await Task.Delay(MIN_SLEEP - sinceLastRun, _stopping);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Releases the wake signal. Latching: a wake that arrives while a run is in flight is kept,
        /// so the next sleep ends immediately and nothing is lost. Further wakes on top of a pending
        /// one collapse into it.
        /// </summary>
        private void SignalWake()
        {
            // Bounded at one with DropWrite, so wakes that pile up while a run is in flight collapse
            // into the single pending signal the next sleep reads.
            _wakeSignal.Writer.TryWrite(true);
        }

        /// <summary>
        /// Claims the slot for one clock aligned tick and, if it wins, runs the job under a running
        /// lock that keeps another container from starting the same job while this one is busy.
        /// Returns true when the job ran here.
        /// A Redis failure, a failover for instance, is logged and skips the slot: the loop survives
        /// it exactly as it survives a throwing Run, and the job runs again on a later slot.
        /// </summary>
        internal async Task<bool> TryRunSlotAsync(DateTime tickUtc, TimeSpan interval)
        {
            try
            {
                return await RunSlotAsync(tickUtc, interval);
            }
            catch (Exception e)
            {
                Log.Error(e, "Cron {Name} could not claim or release its slot, skipping this tick", JobName);
                return false;
            }
        }

        /// <summary>
        /// Test seam. When set, it runs just before the slot claim, which is how a test makes the
        /// Redis side of a slot fail. Null in production.
        /// </summary>
        internal Func<Task>? BeforeSlotClaimAsync { get; set; }

        private async Task<bool> RunSlotAsync(DateTime tickUtc, TimeSpan interval)
        {
            if (BeforeSlotClaimAsync is not null)
            {
                await BeforeSlotClaimAsync();
            }

            var database = Redis.Database;
            var tickUnix = new DateTimeOffset(DateTime.SpecifyKind(tickUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            var slotKey = CronRedisKeys.Slot(JobName, tickUnix);

            // Twice the interval so a container whose clock drifts by a tick still sees the claim,
            // with a floor so that a fast job's claims do not expire before every container has
            // looked at the slot.
            var slotExpiry = interval + interval;
            if (slotExpiry < MIN_SLOT_EXPIRY)
            {
                slotExpiry = MIN_SLOT_EXPIRY;
            }

            if (!await database.StringSetAsync(slotKey, "1", slotExpiry, When.NotExists))
            {
                Log.Debug("Cron {Name} skipped slot {Tick}, another instance claimed it", JobName, tickUnix);
                return false;
            }

            var runningKey = CronRedisKeys.Running(JobName);

            if (!await database.StringSetAsync(runningKey, _instanceId, RUNNING_LOCK_EXPIRY, When.NotExists))
            {
                Log.Warning("Cron {Name} skipped slot {Tick}, previous run still in progress on another instance", JobName, tickUnix);
                return false;
            }

            using var refreshSource = new CancellationTokenSource();
            var refresh = RefreshRunningLockAsync(runningKey, refreshSource.Token);

            try
            {
                await RunOnceAsync();
            }
            finally
            {
                await refreshSource.CancelAsync();

                try
                {
                    await refresh;
                }
                catch (OperationCanceledException)
                {
                    // The refresh loop was told to stop because the run finished.
                }

                // Compare and delete, so a lock that already expired and was retaken by another
                // instance is left alone.
                await Redis.ReleaseLockAsync(runningKey, _instanceId);
            }

            return true;
        }

        /// <summary>
        /// Pushes the running lock's expiry out while the run is in flight, so a run longer than the
        /// lock expiry does not let a second container start the same job. The refresh stops as soon
        /// as the lock is no longer this instance's, so a starved refresh can never extend the lock
        /// another container has since taken.
        /// </summary>
        private async Task RefreshRunningLockAsync(string runningKey, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(RUNNING_LOCK_REFRESH, token);

                try
                {
                    if (!await TryRefreshLockAsync(runningKey, _instanceId, RUNNING_LOCK_EXPIRY))
                    {
                        Log.Warning("Cron {Name} run lost its running lock, another instance holds it now", JobName);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Cron {Name} could not refresh its running lock", JobName);
                }
            }
        }

        /// <summary>
        /// Extends a lock's expiry only while the key still holds the given value. Returns false when
        /// the lock is gone or belongs to someone else, in which case nothing is touched.
        /// </summary>
        internal static async Task<bool> TryRefreshLockAsync(string lockKey, string lockValue, TimeSpan expiry)
        {
            var result = await Redis.Database.ScriptEvaluateAsync(
                REFRESH_LOCK_SCRIPT,
                [lockKey],
                [lockValue, (long)expiry.TotalMilliseconds]);

            return (long)result == 1;
        }

        /// <summary>
        /// Runs the job once. Every failure is logged so that the loop survives it.
        /// </summary>
        internal async Task RunOnceAsync()
        {
            Volatile.Write(ref _running, 1);
            _lastRunStartedUtc = DateTime.UtcNow;
            CronWake.RunningJob.Value = JobName;

            try
            {
                await _run(_stopping);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                // The job observed the shutdown token and stopped where it was told to.
                Log.Information("Cron {Name} run stopped early, the process is shutting down", JobName);
            }
            catch (Exception e)
            {
                Log.Error(e, "Cron {Name} run failed", JobName);
            }
            finally
            {
                CronWake.RunningJob.Value = null;
                Volatile.Write(ref _running, 0);
            }

            WarnIfOverran(DateTime.UtcNow - _lastRunStartedUtc);
        }

        /// <summary>
        /// A run longer than the job's own interval is the first sign the job is falling behind:
        /// nothing else shows it, since the next tick simply waits (or, with a slot claim, is
        /// skipped) rather than piling up or erroring.
        /// </summary>
        private void WarnIfOverran(TimeSpan took)
        {
            TimeSpan interval;

            try
            {
                // A fan out tick is as long as its items: its Interval caps the sleep between ticks
                // and defaults the item lease, and the lease warning covers a slow item.
                if (_cron is null || _fanOut is not null)
                {
                    return;
                }

                interval = _cron.Interval;
            }
            catch (Exception)
            {
                // A broken Interval getter is reported by the loop itself.
                return;
            }

            if (took > interval)
            {
                Log.Warning("Cron {Name} run took {Took}, longer than its {Interval} interval, so it is falling behind", JobName, took, interval);
            }
        }
    }
}

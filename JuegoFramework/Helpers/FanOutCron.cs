using System.Diagnostics;
using StackExchange.Redis;

namespace JuegoFramework.Helpers
{
    /// <summary>
    /// A cron job that spreads one tick's work across the fleet. Every container enumerates the same
    /// items, claims each one in Redis, and processes only the ones it won, so adding containers
    /// adds throughput instead of duplicate work.
    /// <para>
    /// The contract is at least once: <see cref="Enumerate"/> returns only items that still need
    /// work, <see cref="Process"/> must be safe to repeat, and an item can be processed twice when
    /// its processing outlives <see cref="ItemLease"/>, or when another container claims it right
    /// after a successful run released its claim. A job whose enumeration cannot tell "done" from
    /// "still needs work" records the last pass on the item, a timestamp column for instance, and
    /// enumerates on that.
    /// Above roughly 100k items per tick, shard the job rather than enumerate everything.
    /// </para>
    /// <para>
    /// <see cref="Cron.Interval"/> is the cap on the sleep between ticks and the default
    /// <see cref="ItemLease"/>. It is not a bound on a tick's duration: a tick over many items may
    /// legitimately outlast it, so the single runner's "run longer than its interval" warning does
    /// not apply here; the per item lease warning covers the slow case.
    /// </para>
    /// </summary>
    /// <typeparam name="TItem">The type of one unit of work.</typeparam>
    public abstract class FanOutCron<TItem> : Cron, IFanOutJob
    {
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        /// <summary>
        /// How long until this job next has work to do, or null for "use <see cref="Cron.Interval"/>".
        /// The value is a delay rather than a timestamp so that a job can compute it from the
        /// database clock without app and database skew mattering.
        /// The runner sleeps for <c>clamp(value ?? Interval, 250ms, Interval)</c>, so
        /// <see cref="Cron.Interval"/> stays the cap and a "nothing is due" answer cannot spin the loop.
        /// A wake published with <see cref="CronWake.PublishAsync"/> for this job's type name cuts the
        /// current sleep short, so a job that answers here settles its items within about a second
        /// of them becoming due rather than at the next interval.
        /// </summary>
        /// <returns>The delay until the next due work, or null to fall back to <see cref="Cron.Interval"/>.</returns>
        public virtual Task<TimeSpan?> NextDueIn() => Task.FromResult<TimeSpan?>(null);

        Task<TimeSpan?> IFanOutJob.NextDueIn() => NextDueIn();

        /// <summary>
        /// Returns the items that still need work this tick. An exception here is logged by the
        /// runner and ends the tick; nothing is claimed.
        /// </summary>
        /// <param name="stopping">Cancelled when the process is shutting down.</param>
        /// <returns>The items to process.</returns>
        public abstract Task<List<TItem>> Enumerate(CancellationToken stopping);

        /// <summary>
        /// Processes one item. Must be safe to repeat: the same item can come back on a later tick,
        /// and can be taken by another container once its lease lapses.
        /// </summary>
        /// <param name="item">The item to process.</param>
        /// <param name="stopping">Cancelled when the process is shutting down. A long item passes
        /// it to the work it awaits, or polls it, so that the worker can drain. An item that throws
        /// <see cref="OperationCanceledException"/> once this is cancelled has its claim released
        /// rather than counted as a failure, so the next tick anywhere takes it straight away.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public abstract Task Process(TItem item, CancellationToken stopping);

        /// <summary>
        /// How many items this container processes at once. One by default. Above one,
        /// <see cref="Process"/> runs on several threads at the same time, so it must not touch
        /// mutable state on the job instance or in a service shared between items.
        /// </summary>
        public virtual int Concurrency => 1;

        /// <summary>
        /// The Redis key segment identifying one item. Must be stable across containers and unique
        /// per item. The default is the item's ToString, which is right for a number, a string or a
        /// Guid. For a class it is the type name, which would give every item the same key and
        /// process one of them per tick, so override this for any other item type. Duplicate keys
        /// in one tick are logged as an error.
        /// </summary>
        /// <param name="item">The item to identify.</param>
        /// <returns>The key segment for the item.</returns>
        public virtual string ItemKey(TItem item) => item!.ToString()!;

        /// <summary>
        /// How long a claim on an item is held. Defaults to <see cref="Cron.Interval"/>. Set it
        /// above the time the slowest item takes, or another container may start the same item
        /// while this one is still on it.
        /// </summary>
        public virtual TimeSpan ItemLease => Interval;

        /// <summary>
        /// Snapshots everything the shared runner needs for one tick.
        /// </summary>
        internal FanOutJobContext<TItem> BuildContext(CancellationToken stopping) => new(
            GetType().Name,
            _instanceId,
            stopping,
            Concurrency,
            ItemLease,
            Enumerate,
            Process,
            ItemKey);

        private bool _lostEveryClaim;

        bool IFanOutJob.LostEveryClaim => _lostEveryClaim;

        /// <summary>
        /// Enumerates, claims and processes. Implemented by the base class; override
        /// <see cref="Enumerate"/> and <see cref="Process"/> instead.
        /// </summary>
        /// <param name="stopping">Cancelled when the process is shutting down. No new item is
        /// started once it is, and it is handed to every <see cref="Process"/> call.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public sealed override async Task Run(CancellationToken stopping)
        {
            _lostEveryClaim = false;
            _lostEveryClaim = await FanOutRunner.RunAsync(BuildContext(stopping));
        }
    }

    /// <summary>
    /// A fan out job seen without its item type: it ticks on every container (its per item claims
    /// split the work, so it must not claim the tick itself), it answers when its next work is due,
    /// and it is the only kind of job a <see cref="CronWake"/> can wake. Its one member is internal,
    /// so only <see cref="FanOutCron{TItem}"/> can implement it: a job cannot opt out of the once
    /// per tick rule by claiming to be a fan out job.
    /// </summary>
    public interface IFanOutJob
    {
        /// <summary>
        /// See <see cref="FanOutCron{TItem}.NextDueIn"/>.
        /// </summary>
        internal Task<TimeSpan?> NextDueIn();

        /// <summary>
        /// True when the last tick enumerated items and won none of their claims. Every item was
        /// then either in flight on another container or backing off after a failure, so the runner
        /// sleeps the full interval instead of asking <see cref="NextDueIn"/>, which would still
        /// answer "now" for those very items. A wake still cuts that sleep short.
        /// </summary>
        internal bool LostEveryClaim { get; }
    }

    /// <summary>
    /// What <see cref="FanOutRunner"/> needs from a fan out job, snapshotted once per tick, so the
    /// claim and process body works on plain values rather than on the public base class.
    /// </summary>
    internal sealed record FanOutJobContext<TItem>(
        string JobName,
        string InstanceId,
        CancellationToken Stopping,
        int Concurrency,
        TimeSpan ItemLease,
        Func<CancellationToken, Task<List<TItem>>> Enumerate,
        Func<TItem, CancellationToken, Task> Process,
        Func<TItem, string> ItemKey);

    /// <summary>
    /// The enumerate, claim, process body behind <see cref="FanOutCron{TItem}.Run"/>.
    /// </summary>
    internal static class FanOutRunner
    {
        private static readonly TimeSpan MIN_ITEM_LEASE = TimeSpan.FromSeconds(1);

        /// <summary>
        /// An Enumerate slower than this is warned about: it runs on every container on every tick
        /// and on every wake, so a slow one is the first thing to show up on the database.
        /// </summary>
        internal static readonly TimeSpan SLOW_ENUMERATE = TimeSpan.FromSeconds(1);

        /// <summary>
        /// An Enumerate returning more items than this is warned about: every item is one Redis
        /// claim per container per tick, and the documented guidance is to shard well before 100k.
        /// </summary>
        internal const int LARGE_ENUMERATION = 10_000;

        /// <summary>
        /// Runs one fan out tick. An exception from Enumerate propagates to the cron runner, which
        /// logs it; a per item exception is logged and does not stop the other items.
        /// </summary>
        /// <returns>True when items were enumerated and none of their claims was won.</returns>
        internal static async Task<bool> RunAsync<TItem>(FanOutJobContext<TItem> job)
        {
            var enumerateStarted = Stopwatch.GetTimestamp();
            var items = await job.Enumerate(job.Stopping);
            var enumerateTook = Stopwatch.GetElapsedTime(enumerateStarted);

            if (enumerateTook > SLOW_ENUMERATE)
            {
                Log.Warning("Cron {Name} Enumerate took {Took} for {Count} items. It runs on every container on every tick and wake, so index the query it makes", job.JobName, enumerateTook, items.Count);
            }

            if (items.Count > LARGE_ENUMERATION)
            {
                Log.Warning("Cron {Name} enumerated {Count} items in one tick, each one a Redis claim per container. Shard the job before it reaches 100k", job.JobName, items.Count);
            }

            if (items.Count == 0)
            {
                // Same template as the non empty tick below, so one log query covers every tick.
                Log.Information(
                    "Cron {Name} fan out: enumerated {Enumerated}, claimed {Claimed}, skipped {Skipped}, failed {Failed}, released {Released}",
                    job.JobName,
                    0,
                    0,
                    0,
                    0,
                    0);
                return false;
            }

            var lease = job.ItemLease;
            if (lease < MIN_ITEM_LEASE)
            {
                lease = MIN_ITEM_LEASE;
            }

            var claims = await ClaimAsync(job, items, lease);
            var winners = claims.Where(claim => claim.Won).ToList();
            var failed = 0;
            var released = 0;

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = job.Concurrency < 1 ? 1 : job.Concurrency,
                CancellationToken = job.Stopping
            };

            try
            {
                await Parallel.ForEachAsync(winners, options, async (winner, token) =>
                {
                    var started = Stopwatch.GetTimestamp();

                    try
                    {
                        await job.Process(winner.Item, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // The worker is draining and the item stopped where it was told to. Not a
                        // broken item, so its claim must not back off for the lease: release it, and
                        // the next tick anywhere takes it again.
                        Log.Information("Cron {Name} item {ItemKey} stopped early, the process is shutting down", job.JobName, winner.Key);
                        await TryReleaseAsync(job, winner);
                        return;
                    }
                    catch (Exception e)
                    {
                        Interlocked.Increment(ref failed);
                        Log.Error(e, "Cron {Name} failed to process item {ItemKey}", job.JobName, winner.Key);
                        return;
                    }

                    var took = Stopwatch.GetElapsedTime(started);
                    if (took > lease)
                    {
                        // The claim lapsed while the item was still being processed, so another
                        // container may have taken it meanwhile. That is within the at least once
                        // contract, but a job that sees this should raise ItemLease.
                        Log.Warning("Cron {Name} item {ItemKey} took {Took} against a lease of {Lease}, another container may have processed it too", job.JobName, winner.Key, took, lease);
                    }

                    if (await TryReleaseAsync(job, winner))
                    {
                        Interlocked.Increment(ref released);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                Log.Information("Cron {Name} fan out stopped early, the process is shutting down", job.JobName);
            }

            Log.Information(
                "Cron {Name} fan out: enumerated {Enumerated}, claimed {Claimed}, skipped {Skipped}, failed {Failed}, released {Released}",
                job.JobName,
                items.Count,
                winners.Count,
                items.Count - winners.Count,
                failed,
                released);

            return winners.Count == 0;
        }

        /// <summary>
        /// Releases one claim. Compare and delete on this claim's own value, which is unique per
        /// item and per tick, so a release can only ever delete the claim it made; a claim another
        /// container, another tick or a retry made is left alone. A Redis failure here is a warning,
        /// not an item failure: the item is processed either way and the claim lingers until its
        /// lease lapses.
        /// </summary>
        /// <returns>True when the claim was released.</returns>
        private static async Task<bool> TryReleaseAsync<TItem>(FanOutJobContext<TItem> job, ItemClaim<TItem> claim)
        {
            try
            {
                await Redis.ReleaseLockAsync(claim.Key, claim.ClaimValue);
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(e, "Cron {Name} processed item {ItemKey} but could not release its claim", job.JobName, claim.Key);
                return false;
            }
        }

        /// <summary>
        /// Claims every item in one pipelined batch, so the round trips do not add up over a long
        /// item list. Every claim is observed on its own: one that fails counts as not won and is
        /// reported once for the tick, so a partial Redis failure costs this tick some items rather
        /// than stranding the claims that did succeed until their lease lapses.
        /// </summary>
        /// <param name="job">The job's tick snapshot.</param>
        /// <param name="items">The items to claim.</param>
        /// <param name="lease">How long a won claim is held.</param>
        /// <param name="claimCommand">The Redis command one claim issues. Defaults to SET NX PX; a test replaces it to make a claim fail.</param>
        internal static async Task<List<ItemClaim<TItem>>> ClaimAsync<TItem>(
            FanOutJobContext<TItem> job,
            List<TItem> items,
            TimeSpan lease,
            Func<IBatch, string, string, TimeSpan, Task<bool>>? claimCommand = null)
        {
            claimCommand ??= static (batch, key, value, ttl) => batch.StringSetAsync(key, value, ttl, When.NotExists);

            var database = Redis.Database;
            var batch = database.CreateBatch();
            var pending = new List<(TItem Item, string Key, string ClaimValue, Task<bool> Claim)>(items.Count);
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = 0;
            string? firstDuplicate = null;

            foreach (var item in items)
            {
                var key = CronRedisKeys.Item(job.JobName, job.ItemKey(item));

                if (!seenKeys.Add(key))
                {
                    duplicates++;
                    firstDuplicate ??= key;
                }

                // Unique per item and per tick, so the release after a successful Process can only
                // delete this very claim, even when the same instance reclaims the item later.
                var claimValue = $"{job.InstanceId}:{Guid.NewGuid():N}";
                Task<bool> claim;

                try
                {
                    claim = claimCommand(batch, key, claimValue, lease);
                }
                catch (Exception e)
                {
                    // The client can reject a command before it is ever sent, a bad expiry for
                    // instance. That is one more failed claim, not a failed tick.
                    claim = Task.FromException<bool>(e);
                }

                pending.Add((item, key, claimValue, claim));
            }

            if (duplicates > 0)
            {
                // The second claim on a key loses to the first, so every duplicate is silently
                // skipped. Almost always a class item with the default ItemKey, which is its type
                // name, or an Enumerate that returns the same item twice.
                Log.Error("Cron {Name} enumerated {Count} items whose ItemKey repeats another's, for example {Key}. Only one of each is processed; override ItemKey", job.JobName, duplicates, firstDuplicate);
            }

            batch.Execute();

            var claims = new List<ItemClaim<TItem>>(pending.Count);
            var faulted = 0;
            Exception? firstFailure = null;

            foreach (var entry in pending)
            {
                var won = false;

                try
                {
                    won = await entry.Claim;
                }
                catch (Exception e)
                {
                    faulted++;
                    firstFailure ??= e;
                }

                claims.Add(new ItemClaim<TItem>(entry.Item, entry.Key, entry.ClaimValue, won));
            }

            if (faulted > 0)
            {
                Log.Error(firstFailure, "Cron {Name} could not claim {Count} of {Total} items this tick", job.JobName, faulted, pending.Count);
            }

            return claims;
        }

        /// <summary>
        /// One item, its Redis key, the value this tick claimed it with, and whether the claim was won.
        /// </summary>
        internal sealed record ItemClaim<TItem>(TItem Item, string Key, string ClaimValue, bool Won);
    }
}

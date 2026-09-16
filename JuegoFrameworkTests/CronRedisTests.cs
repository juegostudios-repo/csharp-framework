using System.Collections.Concurrent;
using System.Diagnostics;
using JuegoFramework.Helpers;
using StackExchange.Redis;

namespace JuegoFrameworkTests
{
    /// <summary>
    /// The Redis backed half of the cron runner: single runner slot claims, the wake channel and the
    /// fan out claim loop. Every test here is skipped when REDIS_CONNECTION_STRING is not set.
    /// Each job type is used by one test only, so the key namespaces of the tests cannot collide.
    /// </summary>
    [Collection(CronCollection.NAME)]
    public class CronRedisTests : IAsyncLifetime
    {
        private readonly RedisFixture _redis;

        public CronRedisTests(RedisFixture redis)
        {
            _redis = redis;
        }

        public async Task InitializeAsync()
        {
            if (!RedisFixture.IsRedisConfigured)
            {
                return;
            }

            await _redis.ResetCronKeysAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // Task A4. Single runner slots.

        [RedisFact]
        public async Task Two_Runners_On_The_Same_Slot_Run_The_Job_Once()
        {
            SlotCron.Reset();

            var first = new SlotCron();
            var second = new SlotCron();
            var tick = CronRunner.AlignedNextTick(DateTime.UtcNow, SlotCron.INTERVAL);

            var results = await Task.WhenAll(
                RunnerFor(first).TryRunSlotAsync(tick, SlotCron.INTERVAL),
                RunnerFor(second).TryRunSlotAsync(tick, SlotCron.INTERVAL));

            Assert.Single(results, won => won);
            Assert.Equal(1, SlotCron.RunCount);
        }

        [RedisFact]
        public async Task A_Run_That_Outlives_Its_Slot_Blocks_The_Next_Slot_Elsewhere()
        {
            GatedSlotCron.Reset();

            var slow = new GatedSlotCron();
            var other = new GatedSlotCron();
            var tick = CronRunner.AlignedNextTick(DateTime.UtcNow, GatedSlotCron.INTERVAL);

            var slowRun = RunnerFor(slow).TryRunSlotAsync(tick, GatedSlotCron.INTERVAL);

            await WaitUntil(() => GatedSlotCron.Started);

            // The next slot is free, but the previous run is still in flight, so nobody may start it.
            var nextSlot = tick + GatedSlotCron.INTERVAL;
            Assert.False(await RunnerFor(other).TryRunSlotAsync(nextSlot, GatedSlotCron.INTERVAL));
            Assert.Equal(1, GatedSlotCron.RunCount);

            GatedSlotCron.Gate.TrySetResult();
            Assert.True(await slowRun);

            // Once the running lock is gone the slot is runnable again.
            Assert.True(await RunnerFor(other).TryRunSlotAsync(nextSlot + GatedSlotCron.INTERVAL, GatedSlotCron.INTERVAL));
            Assert.Equal(2, GatedSlotCron.RunCount);
        }

        [RedisFact]
        public async Task A_Redis_Failure_On_A_Slot_Does_Not_Kill_The_Loop()
        {
            FaultedSlotCron.Reset();

            var job = new FaultedSlotCron();
            using var stopping = new CancellationTokenSource();
            var runner = RunnerFor(job, stopping.Token);
            var faults = 0;

            // The first slot fails the way a Redis failover would, the later ones are healthy.
            runner.BeforeSlotClaimAsync = () => Interlocked.Increment(ref faults) == 1
                ? throw new InvalidOperationException("Redis is away on purpose")
                : Task.CompletedTask;

            var loop = Task.Run(runner.RunLoopAsync);

            try
            {
                await WaitUntil(() => FaultedSlotCron.RunCount >= 1);
            }
            finally
            {
                await stopping.CancelAsync();
                await loop;
            }

            Assert.True(loop.IsCompletedSuccessfully, "the loop faulted instead of skipping the slot");
            Assert.True(faults >= 2, "the loop did not reach a second slot");
            Assert.True(FaultedSlotCron.RunCount >= 1);
        }

        [RedisFact]
        public async Task A_Running_Lock_Held_By_Someone_Else_Is_Not_Refreshed()
        {
            var key = CronRedisKeys.Running("RefreshProbe");

            await _redis.Database.StringSetAsync(key, "another-instance", TimeSpan.FromSeconds(5));

            Assert.False(await CronRunner.TryRefreshLockAsync(key, "this-instance", TimeSpan.FromMinutes(10)));

            var expiry = await _redis.Database.KeyTimeToLiveAsync(key);
            Assert.NotNull(expiry);
            Assert.True(expiry!.Value < TimeSpan.FromSeconds(10), $"the expiry was extended to {expiry}");

            // The owner does get its expiry pushed out.
            Assert.True(await CronRunner.TryRefreshLockAsync(key, "another-instance", TimeSpan.FromMinutes(10)));
            Assert.True((await _redis.Database.KeyTimeToLiveAsync(key))!.Value > TimeSpan.FromSeconds(10));

            // A lock that is gone cannot be refreshed either.
            await _redis.Database.KeyDeleteAsync(key);
            Assert.False(await CronRunner.TryRefreshLockAsync(key, "another-instance", TimeSpan.FromMinutes(10)));
        }

        // Task A5. Dynamic sleep and the wake channel.

        [RedisFact]
        public async Task A_Wake_Cuts_A_Long_Sleep_Short()
        {
            WakeCron.Reset();

            var job = new WakeCron();
            using var stopping = new CancellationTokenSource();
            var loop = Task.Run(RunnerFor(job, stopping.Token).RunLoopAsync);

            try
            {
                // Let the loop reach its sleep and the subscription settle.
                await Task.Delay(300);
                Assert.Equal(0, WakeCron.RunCount);

                var stopwatch = Stopwatch.StartNew();
                await CronWake.PublishAsync<WakeCron>();
                await WaitUntil(() => WakeCron.RunCount >= 1);
                stopwatch.Stop();

                Assert.True(stopwatch.ElapsedMilliseconds < 500, $"the wake took {stopwatch.ElapsedMilliseconds} ms to land");
            }
            finally
            {
                await stopping.CancelAsync();
                await loop;
            }
        }

        [RedisFact]
        public async Task A_Wake_During_A_Run_Causes_An_Immediate_Rerun()
        {
            GatedWakeCron.Reset();

            var job = new GatedWakeCron();
            using var stopping = new CancellationTokenSource();
            var loop = Task.Run(RunnerFor(job, stopping.Token).RunLoopAsync);

            try
            {
                await Task.Delay(300);

                // The first wake starts the run, which then blocks on the gate.
                await CronWake.PublishAsync<GatedWakeCron>();
                await WaitUntil(() => GatedWakeCron.Started);

                // The second wake arrives while the run is in flight, so it has to be kept.
                await CronWake.PublishAsync<GatedWakeCron>();
                await Task.Delay(200);
                GatedWakeCron.Gate.TrySetResult();

                var stopwatch = Stopwatch.StartNew();
                await WaitUntil(() => GatedWakeCron.RunCount >= 2);
                stopwatch.Stop();

                // The wake was not lost: the second run started without waiting out the interval,
                // and no sooner than the minimum sleep after the first one.
                Assert.True(stopwatch.ElapsedMilliseconds < 1500, $"the second run took {stopwatch.ElapsedMilliseconds} ms");
            }
            finally
            {
                GatedWakeCron.Gate.TrySetResult();
                await stopping.CancelAsync();
                await loop;
            }
        }

        [RedisFact]
        public async Task A_Tick_That_Won_Nothing_Sleeps_Its_Interval_Until_Woken()
        {
            LostClaimCron.Reset();

            // Another container holds the only item, so every tick here loses its claim.
            await _redis.Database.StringSetAsync(CronRedisKeys.Item(nameof(LostClaimCron), "1"), "elsewhere", TimeSpan.FromSeconds(60));

            var job = new LostClaimCron();
            using var stopping = new CancellationTokenSource();
            var loop = Task.Run(RunnerFor(job, stopping.Token).RunLoopAsync);

            try
            {
                // NextDueIn answers "now", so without the rule this would enumerate at the 250 ms
                // floor: four or five times in this window. With it, the first tick loses and the
                // loop sleeps its 30 s interval.
                await Task.Delay(1200);
                Assert.Equal(1, LostClaimCron.EnumerateCount);

                // A wake still cuts that sleep short.
                var stopwatch = Stopwatch.StartNew();
                await CronWake.PublishAsync<LostClaimCron>();
                await WaitUntil(() => LostClaimCron.EnumerateCount >= 2);
                stopwatch.Stop();

                Assert.True(stopwatch.ElapsedMilliseconds < 500, $"the wake took {stopwatch.ElapsedMilliseconds} ms to land");
            }
            finally
            {
                await stopping.CancelAsync();
                await loop;
            }
        }

        [RedisFact]
        public async Task A_Job_Waking_Itself_From_Its_Own_Run_Is_Not_Rerun_But_Another_Job_Is()
        {
            SelfWakeCron.Reset();
            SelfWakeTargetCron.Reset();

            var job = new SelfWakeCron();
            var target = new SelfWakeTargetCron();
            using var stopping = new CancellationTokenSource();
            var loop = Task.Run(RunnerFor(job, stopping.Token).RunLoopAsync);
            var targetLoop = Task.Run(RunnerFor(target, stopping.Token).RunLoopAsync);

            try
            {
                await Task.Delay(300);
                Assert.Equal(0, SelfWakeCron.RunCount);

                await CronWake.PublishAsync<SelfWakeCron>();
                await WaitUntil(() => SelfWakeCron.RunCount >= 1);

                // Process published a wake for its own job and one for the target. The target's
                // sleep is cut short; the job's own wake is dropped, so it does not rerun.
                await WaitUntil(() => SelfWakeTargetCron.RunCount >= 1);
                await Task.Delay(1000);
                Assert.Equal(1, SelfWakeCron.RunCount);
            }
            finally
            {
                await stopping.CancelAsync();
                await Task.WhenAll(loop, targetLoop);
            }
        }

        [RedisFact]
        public async Task A_NextDueIn_Of_Zero_Does_Not_Spin()
        {
            AlwaysDueCron.Reset();

            var job = new AlwaysDueCron();
            using var stopping = new CancellationTokenSource();
            var loop = Task.Run(RunnerFor(job, stopping.Token).RunLoopAsync);

            try
            {
                await Task.Delay(1200);
            }
            finally
            {
                await stopping.CancelAsync();
                await loop;
            }

            // 1200 ms of a 250 ms floor is at most five runs, plus one for the boundary.
            Assert.InRange(AlwaysDueCron.RunCount, 2, 6);
        }

        [RedisFact]
        public async Task A_NextDueIn_That_Throws_Falls_Back_To_The_Interval()
        {
            BrokenNextDueInCron.Reset();

            var job = new BrokenNextDueInCron();
            using var stopping = new CancellationTokenSource();
            var loop = Task.Run(RunnerFor(job, stopping.Token).RunLoopAsync);

            try
            {
                await WaitUntil(() => BrokenNextDueInCron.RunCount >= 2);
            }
            finally
            {
                await stopping.CancelAsync();
                await loop;
            }

            Assert.True(BrokenNextDueInCron.RunCount >= 2, $"expected the loop to keep running, got {BrokenNextDueInCron.RunCount} runs");
        }

        // Task A6. Fan out.

        [RedisFact]
        public async Task Two_Runners_On_The_Default_Release_Path_Process_Every_Item_At_Least_Once()
        {
            DefaultReleaseFanOutCron.Reset();

            var first = new DefaultReleaseFanOutCron();
            var second = new DefaultReleaseFanOutCron();

            await Task.WhenAll(first.Run(CancellationToken.None), second.Run(CancellationToken.None));

            var processed = DefaultReleaseFanOutCron.Processed.ToList();

            // At least once is the contract on the default path: a container enumerating at the same
            // moment can retake an item the other has just finished and released.
            Assert.Equal(DefaultReleaseFanOutCron.ITEM_COUNT, processed.Distinct().Count());
            Assert.True(processed.Count >= DefaultReleaseFanOutCron.ITEM_COUNT, $"only {processed.Count} of {DefaultReleaseFanOutCron.ITEM_COUNT} items were processed");
        }

        [RedisFact]
        public async Task On_The_Default_Path_An_Item_Is_Claimable_Again_Right_After_A_Success()
        {
            DefaultReleaseFanOutCron.Reset();

            var job = new DefaultReleaseFanOutCron();

            await job.Run(CancellationToken.None);
            Assert.Equal(DefaultReleaseFanOutCron.ITEM_COUNT, DefaultReleaseFanOutCron.Processed.Count);

            // The claims were released, so the very next tick takes the same items again.
            await job.Run(CancellationToken.None);
            Assert.Equal(DefaultReleaseFanOutCron.ITEM_COUNT * 2, DefaultReleaseFanOutCron.Processed.Count);
        }

        [RedisFact]
        public async Task A_Failed_Claim_Costs_The_Tick_Its_Items_Rather_Than_Aborting()
        {
            var job = new ClaimProbeFanOutCron();
            var context = job.BuildContext(CancellationToken.None);

            // A negative lease is what Redis rejects, so every claim in the batch faults. The helper
            // has to hand back one entry per item, none of them won, and must not throw.
            var claims = await FanOutRunner.ClaimAsync(context, [1, 2, 3], TimeSpan.FromSeconds(-5));

            Assert.Equal(3, claims.Count);
            Assert.DoesNotContain(claims, claim => claim.Won);

            // One claim of three fails: the other two are still won and still claimed in Redis, so
            // the tick loses one item rather than stranding the claims it did take.
            var partial = await FanOutRunner.ClaimAsync(
                context,
                [1, 2, 3],
                TimeSpan.FromSeconds(30),
                (batch, key, value, ttl) => key.EndsWith(":2", StringComparison.Ordinal)
                    ? Task.FromException<bool>(new TimeoutException("this claim failed on purpose"))
                    : batch.StringSetAsync(key, value, ttl, When.NotExists));

            Assert.Equal([true, false, true], partial.Select(claim => claim.Won).ToList());
            Assert.True(await _redis.Database.KeyExistsAsync(partial[0].Key));
            Assert.False(await _redis.Database.KeyExistsAsync(partial[1].Key));
        }

        [RedisFact]
        public async Task Each_Claim_Carries_Its_Own_Value_So_A_Release_Cannot_Delete_A_Later_One()
        {
            var job = new ClaimProbeFanOutCron();
            var context = job.BuildContext(CancellationToken.None);
            var lease = TimeSpan.FromMilliseconds(600);

            var first = Assert.Single(await FanOutRunner.ClaimAsync(context, [9], lease));
            Assert.True(first.Won);

            await Task.Delay(lease + TimeSpan.FromMilliseconds(300));

            // The same instance retakes the item on a later tick, so the two claims must not share a
            // value, or the first Process finishing would delete the second claim.
            var second = Assert.Single(await FanOutRunner.ClaimAsync(context, [9], lease));
            Assert.True(second.Won);
            Assert.NotEqual(first.ClaimValue, second.ClaimValue);

            await Redis.ReleaseLockAsync(first.Key, first.ClaimValue);
            Assert.True(await _redis.Database.KeyExistsAsync(second.Key), "releasing the stale claim deleted the live one");

            await Redis.ReleaseLockAsync(second.Key, second.ClaimValue);
            Assert.False(await _redis.Database.KeyExistsAsync(second.Key));
        }

        [RedisFact]
        public async Task A_Failed_Item_Keeps_Its_Claim_For_The_Lease_And_Is_Taken_Again_After()
        {
            LeasedFanOutCron.Reset();

            var job = new LeasedFanOutCron();

            await job.Run(CancellationToken.None);
            Assert.Equal(1, LeasedFanOutCron.RunCount);

            // The failed item's claim still holds, so the item is skipped: this is the back-off.
            await job.Run(CancellationToken.None);
            Assert.Equal(1, LeasedFanOutCron.RunCount);

            await Task.Delay(LeasedFanOutCron.LEASE + TimeSpan.FromMilliseconds(400));

            await job.Run(CancellationToken.None);
            Assert.Equal(2, LeasedFanOutCron.RunCount);
        }

        [RedisFact]
        public async Task Class_Items_With_The_Default_Key_Collapse_To_One_And_Are_Reported()
        {
            OpaqueItemFanOutCron.Reset();

            // Three distinct items whose default ItemKey is the type name, so they share one claim.
            await new OpaqueItemFanOutCron().Run(CancellationToken.None);

            Assert.Equal(1, OpaqueItemFanOutCron.Processed.Count);
        }

        [RedisFact]
        public async Task A_Throwing_Item_Does_Not_Stop_The_Others()
        {
            ThrowingFanOutCron.Reset();

            var job = new ThrowingFanOutCron();

            await job.Run(CancellationToken.None);

            Assert.Equal([1, 2, 4, 5], ThrowingFanOutCron.Processed.OrderBy(item => item).ToList());
        }

        [RedisFact]
        public async Task An_Item_Stopped_By_Shutdown_Has_Its_Claim_Released()
        {
            CancelledItemFanOutCron.Reset();

            var job = new CancelledItemFanOutCron();
            using var stopping = new CancellationTokenSource();
            var run = job.Run(stopping.Token);

            await WaitUntil(() => CancelledItemFanOutCron.Started);
            Assert.True(await _redis.Database.KeyExistsAsync(CronRedisKeys.Item(nameof(CancelledItemFanOutCron), "1")));

            await stopping.CancelAsync();
            await run;

            // Stopping mid-item is not a failure: the claim is released so the next tick anywhere
            // takes the item straight away instead of waiting out the lease.
            Assert.False(await _redis.Database.KeyExistsAsync(CronRedisKeys.Item(nameof(CancelledItemFanOutCron), "1")));
        }

        [RedisFact]
        public async Task A_Claim_Is_Released_After_A_Success_And_Kept_After_A_Failure()
        {
            var job = new ReleaseFanOutCron();

            await job.Run(CancellationToken.None);

            Assert.False(await _redis.Database.KeyExistsAsync(CronRedisKeys.Item(nameof(ReleaseFanOutCron), "ok")));
            Assert.True(await _redis.Database.KeyExistsAsync(CronRedisKeys.Item(nameof(ReleaseFanOutCron), "bad")));
        }

        private static CronRunner RunnerFor(Cron job, CancellationToken stopping = default)
        {
            return new CronRunner(job, now => now + job.Interval, stopping, redisConfigured: true);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("condition was not met in time");
                }

                await Task.Delay(10);
            }
        }

        public sealed class SlotCron : Cron
        {
            internal static readonly TimeSpan INTERVAL = TimeSpan.FromSeconds(30);

            private static int _runCount;

            public static int RunCount => Volatile.Read(ref _runCount);

            public static void Reset() => Volatile.Write(ref _runCount, 0);

            public override TimeSpan Interval => INTERVAL;

            public override Task Run(CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);
                return Task.CompletedTask;
            }
        }

        public sealed class GatedSlotCron : Cron
        {
            internal static readonly TimeSpan INTERVAL = TimeSpan.FromSeconds(30);

            private static int _runCount;
            private static int _started;

            public static TaskCompletionSource Gate { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public static int RunCount => Volatile.Read(ref _runCount);

            public static bool Started => Volatile.Read(ref _started) == 1;

            public static void Reset()
            {
                Volatile.Write(ref _runCount, 0);
                Volatile.Write(ref _started, 0);
                Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public override TimeSpan Interval => INTERVAL;

            public override async Task Run(CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);

                // Only the first run waits; the later ones just have to finish.
                if (Interlocked.Exchange(ref _started, 1) == 0)
                {
                    await Gate.Task;
                }
            }
        }

        public sealed class FaultedSlotCron : Cron
        {
            private static int _runCount;

            public static int RunCount => Volatile.Read(ref _runCount);

            public static void Reset() => Volatile.Write(ref _runCount, 0);

            // Short, so the loop reaches its second slot inside the test.
            public override TimeSpan Interval => TimeSpan.FromSeconds(1);

            public override Task Run(CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);
                return Task.CompletedTask;
            }
        }

        public sealed class WakeCron : FanOutCron<int>
        {
            private static int _runCount;

            public static int RunCount => Volatile.Read(ref _runCount);

            public static void Reset() => Volatile.Write(ref _runCount, 0);

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<TimeSpan?> NextDueIn() => Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(30));

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 1 });

            public override Task Process(int item, CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);
                return Task.CompletedTask;
            }
        }

        public sealed class GatedWakeCron : FanOutCron<int>
        {
            private static int _runCount;
            private static int _started;

            public static TaskCompletionSource Gate { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public static int RunCount => Volatile.Read(ref _runCount);

            public static bool Started => Volatile.Read(ref _started) == 1;

            public static void Reset()
            {
                Volatile.Write(ref _runCount, 0);
                Volatile.Write(ref _started, 0);
                Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<TimeSpan?> NextDueIn() => Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(30));

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 1 });

            public override async Task Process(int item, CancellationToken stopping)
            {
                if (Interlocked.Exchange(ref _started, 1) == 0)
                {
                    await Gate.Task;
                }

                Interlocked.Increment(ref _runCount);
            }
        }

        public sealed class LostClaimCron : FanOutCron<int>
        {
            private static int _enumerateCount;

            public static int EnumerateCount => Volatile.Read(ref _enumerateCount);

            public static void Reset() => Volatile.Write(ref _enumerateCount, 0);

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<TimeSpan?> NextDueIn() => Task.FromResult<TimeSpan?>(TimeSpan.Zero);

            public override Task<List<int>> Enumerate(CancellationToken stopping)
            {
                Interlocked.Increment(ref _enumerateCount);
                return Task.FromResult(new List<int> { 1 });
            }

            public override Task Process(int item, CancellationToken stopping) => throw new InvalidOperationException("the claim is never won, so this never runs");
        }

        public sealed class SelfWakeCron : FanOutCron<int>
        {
            private static int _runCount;

            public static int RunCount => Volatile.Read(ref _runCount);

            public static void Reset() => Volatile.Write(ref _runCount, 0);

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<TimeSpan?> NextDueIn() => Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(30));

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 1 });

            public override async Task Process(int item, CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);
                await CronWake.PublishAsync<SelfWakeCron>();
                await CronWake.PublishAsync<SelfWakeTargetCron>();
            }
        }

        public sealed class SelfWakeTargetCron : FanOutCron<int>
        {
            private static int _runCount;

            public static int RunCount => Volatile.Read(ref _runCount);

            public static void Reset() => Volatile.Write(ref _runCount, 0);

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<TimeSpan?> NextDueIn() => Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(30));

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 1 });

            public override Task Process(int item, CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);
                return Task.CompletedTask;
            }
        }

        public sealed class AlwaysDueCron : FanOutCron<int>
        {
            private static int _runCount;

            public static int RunCount => Volatile.Read(ref _runCount);

            public static void Reset() => Volatile.Write(ref _runCount, 0);

            public override TimeSpan Interval => TimeSpan.FromSeconds(5);

            public override Task<TimeSpan?> NextDueIn() => Task.FromResult<TimeSpan?>(TimeSpan.Zero);

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 1 });

            public override Task Process(int item, CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);
                return Task.CompletedTask;
            }
        }

        public sealed class BrokenNextDueInCron : FanOutCron<int>
        {
            private static int _runCount;

            public static int RunCount => Volatile.Read(ref _runCount);

            public static void Reset() => Volatile.Write(ref _runCount, 0);

            // Short, so the fallback to the interval is visible inside the test.
            public override TimeSpan Interval => TimeSpan.FromMilliseconds(300);

            public override Task<TimeSpan?> NextDueIn() => throw new InvalidOperationException("the hook is broken on purpose");

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 1 });

            public override Task Process(int item, CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Two instances of this share one item list, so the tests over it see the real at least
        /// once behaviour of the release path.
        /// </summary>
        public sealed class DefaultReleaseFanOutCron : FanOutCron<int>
        {
            internal const int ITEM_COUNT = 40;

            public static ConcurrentQueue<int> Processed { get; } = new();

            public static void Reset() => Processed.Clear();

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override int Concurrency => 4;

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(Enumerable.Range(1, ITEM_COUNT).ToList());

            public override Task Process(int item, CancellationToken stopping)
            {
                Processed.Enqueue(item);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Only used to build a context for the claim helper tests; its Run is never called.
        /// </summary>
        public sealed class ClaimProbeFanOutCron : FanOutCron<int>
        {
            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int>());

            public override Task Process(int item, CancellationToken stopping) => Task.CompletedTask;
        }

        public sealed class LeasedFanOutCron : FanOutCron<int>
        {
            internal static readonly TimeSpan LEASE = TimeSpan.FromSeconds(1);

            private static int _runCount;

            public static int RunCount => Volatile.Read(ref _runCount);

            public static void Reset() => Volatile.Write(ref _runCount, 0);

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override TimeSpan ItemLease => LEASE;

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 7 });

            public override Task Process(int item, CancellationToken stopping)
            {
                Interlocked.Increment(ref _runCount);
                throw new InvalidOperationException("fails on purpose, so the claim is kept for the lease");
            }
        }

        public sealed class OpaqueItem
        {
            public int Id { get; init; }
        }

        public sealed class OpaqueItemFanOutCron : FanOutCron<OpaqueItem>
        {
            public static ConcurrentQueue<int> Processed { get; } = new();

            public static void Reset() => Processed.Clear();

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<List<OpaqueItem>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<OpaqueItem>
            {
                new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 },
            });

            public override Task Process(OpaqueItem item, CancellationToken stopping)
            {
                Processed.Enqueue(item.Id);
                return Task.CompletedTask;
            }
        }

        public sealed class ThrowingFanOutCron : FanOutCron<int>
        {
            public static ConcurrentQueue<int> Processed { get; } = new();

            public static void Reset() => Processed.Clear();

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 1, 2, 3, 4, 5 });

            public override Task Process(int item, CancellationToken stopping)
            {
                if (item == 3)
                {
                    throw new InvalidOperationException("item three fails on purpose");
                }

                Processed.Enqueue(item);
                return Task.CompletedTask;
            }
        }

        public sealed class CancelledItemFanOutCron : FanOutCron<int>
        {
            private static int _started;

            public static bool Started => Volatile.Read(ref _started) == 1;

            public static void Reset() => Volatile.Write(ref _started, 0);

            public override TimeSpan Interval => TimeSpan.FromMinutes(5);

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int> { 1 });

            public override async Task Process(int item, CancellationToken stopping)
            {
                Volatile.Write(ref _started, 1);
                await Task.Delay(Timeout.Infinite, stopping);
            }
        }

        public sealed class ReleaseFanOutCron : FanOutCron<string>
        {
            public override TimeSpan Interval => TimeSpan.FromMinutes(5);

            public override Task<List<string>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<string> { "ok", "bad" });

            public override Task Process(string item, CancellationToken stopping)
            {
                if (item == "bad")
                {
                    throw new InvalidOperationException("the bad item fails on purpose");
                }

                return Task.CompletedTask;
            }
        }
    }
}

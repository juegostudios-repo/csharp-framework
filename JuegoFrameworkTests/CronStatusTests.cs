using JuegoFramework.Helpers;

namespace JuegoFrameworkTests
{
    /// <summary>
    /// The status hash and run history the runner writes, and the reads that serve them. Every
    /// test here is skipped when REDIS_CONNECTION_STRING is not set. Each job type is used by one
    /// test only, so the key namespaces of the tests cannot collide.
    /// </summary>
    [Collection(CronCollection.NAME)]
    public class CronStatusTests : IAsyncLifetime
    {
        private readonly RedisFixture _redis;

        public CronStatusTests(RedisFixture redis)
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

        [RedisFact]
        public async Task A_Run_That_Succeeds_Is_Recorded_In_The_Status_And_The_History()
        {
            var job = new StatusOkCron();
            var runner = RunnerFor(job);

            await CronStatus.RegisterAsync(runner.JobName, CronStatus.KIND_INTERVAL, job.Interval, null, null);
            await runner.RunOnceAsync();

            var status = await CronStatus.GetAsync(runner.JobName);

            Assert.NotNull(status);
            Assert.Equal(CronStatus.KIND_INTERVAL, status.Kind);
            Assert.Equal(job.Interval, status.Interval);
            Assert.Null(status.Expression);
            Assert.Null(status.Concurrency);
            Assert.Equal(CronStatus.OUTCOME_OK, status.LastOutcome);
            Assert.Null(status.LastError);
            Assert.Equal(CronStatus.INSTANCE_ID, status.LastInstance);
            Assert.NotNull(status.LastStartedAt);
            Assert.NotNull(status.LastFinishedAt);
            Assert.False(status.Running);
            Assert.Null(status.LastTick);
            Assert.Null(status.StoppedAt);

            var runs = await CronStatus.RunsAsync(runner.JobName);

            var run = Assert.Single(runs);
            Assert.Equal(CronStatus.OUTCOME_OK, run.Outcome);
            Assert.Null(run.Error);
            Assert.Equal(CronStatus.INSTANCE_ID, run.Instance);
            Assert.Equal(status.LastStartedAt, run.StartedAt);
            Assert.Equal(status.LastFinishedAt, run.FinishedAt);
            Assert.True(run.Duration >= TimeSpan.Zero);
        }

        [RedisFact]
        public async Task A_Run_That_Throws_Records_The_Error()
        {
            var runner = RunnerFor(new StatusFailingCron());

            await CronStatus.RegisterAsync(runner.JobName, CronStatus.KIND_INTERVAL, TimeSpan.FromSeconds(30), null, null);
            await runner.RunOnceAsync();

            var status = await CronStatus.GetAsync(runner.JobName);

            Assert.NotNull(status);
            Assert.Equal(CronStatus.OUTCOME_FAILED, status.LastOutcome);
            Assert.Equal("InvalidOperationException: boom", status.LastError);

            var run = Assert.Single(await CronStatus.RunsAsync(runner.JobName));
            Assert.Equal(CronStatus.OUTCOME_FAILED, run.Outcome);
            Assert.Equal("InvalidOperationException: boom", run.Error);
        }

        [RedisFact]
        public async Task A_Run_Shows_As_Running_Until_It_Finishes()
        {
            StatusGatedCron.Reset();

            var runner = RunnerFor(new StatusGatedCron());

            await CronStatus.RegisterAsync(runner.JobName, CronStatus.KIND_INTERVAL, TimeSpan.FromSeconds(30), null, null);

            var run = runner.RunOnceAsync();
            await WaitUntilAsync(async () => (await CronStatus.GetAsync(runner.JobName))?.Running == true);

            StatusGatedCron.Gate.TrySetResult();
            await run;

            var status = await CronStatus.GetAsync(runner.JobName);
            Assert.NotNull(status);
            Assert.False(status.Running);
        }

        [RedisFact]
        public async Task An_Empty_Fan_Out_Tick_Refreshes_The_Status_But_Is_Not_Kept_In_The_History()
        {
            StatusFanOutCron.ItemCount = 0;

            var job = new StatusFanOutCron();
            var runner = RunnerFor(job);

            await CronStatus.RegisterAsync(runner.JobName, CronStatus.KIND_FAN_OUT, job.Interval, null, job.Concurrency);
            await runner.RunOnceAsync();

            var status = await CronStatus.GetAsync(runner.JobName);

            Assert.NotNull(status);
            Assert.Equal(CronStatus.KIND_FAN_OUT, status.Kind);
            Assert.Equal(job.Concurrency, status.Concurrency);
            Assert.Equal(new FanOutTick(0, 0, 0, 0, 0), status.LastTick);
            Assert.Equal(CronStatus.OUTCOME_OK, status.LastOutcome);
            Assert.Empty(await CronStatus.RunsAsync(runner.JobName));

            StatusFanOutCron.ItemCount = 3;
            await runner.RunOnceAsync();

            status = await CronStatus.GetAsync(runner.JobName);

            Assert.NotNull(status);
            Assert.Equal(new FanOutTick(3, 3, 0, 0, 3), status.LastTick);

            var run = Assert.Single(await CronStatus.RunsAsync(runner.JobName));
            Assert.Equal(new FanOutTick(3, 3, 0, 0, 3), run.Tick);
        }

        [RedisFact]
        public async Task A_Fan_Out_Tick_Whose_Enumerate_Throws_Is_Kept_Without_The_Previous_Counts()
        {
            StatusThrowingEnumerateFanOutCron.Throw = false;

            var runner = RunnerFor(new StatusThrowingEnumerateFanOutCron());

            await CronStatus.RegisterAsync(runner.JobName, CronStatus.KIND_FAN_OUT, TimeSpan.FromSeconds(30), null, 1);
            await runner.RunOnceAsync();

            StatusThrowingEnumerateFanOutCron.Throw = true;
            await runner.RunOnceAsync();

            var runs = await CronStatus.RunsAsync(runner.JobName);

            Assert.Equal(2, runs.Count);
            Assert.Equal(CronStatus.OUTCOME_FAILED, runs[0].Outcome);
            Assert.Null(runs[0].Tick);
            Assert.Equal(CronStatus.OUTCOME_OK, runs[1].Outcome);
            Assert.Equal(2, runs[1].Tick?.Claimed);
        }

        [RedisFact]
        public async Task Runs_Page_Newest_First_Without_Overlap()
        {
            var runner = RunnerFor(new StatusPagedCron());

            await CronStatus.RegisterAsync(runner.JobName, CronStatus.KIND_INTERVAL, TimeSpan.FromSeconds(30), null, null);

            for (var i = 0; i < 5; i++)
            {
                await runner.RunOnceAsync();
            }

            var firstPage = await CronStatus.RunsAsync(runner.JobName, count: 2);
            var secondPage = await CronStatus.RunsAsync(runner.JobName, count: 2, before: firstPage[^1].Id);
            var thirdPage = await CronStatus.RunsAsync(runner.JobName, count: 2, before: secondPage[^1].Id);
            var fourthPage = await CronStatus.RunsAsync(runner.JobName, count: 2, before: thirdPage[^1].Id);

            Assert.Equal(2, firstPage.Count);
            Assert.Equal(2, secondPage.Count);
            Assert.Single(thirdPage);
            Assert.Empty(fourthPage);

            var ids = firstPage.Concat(secondPage).Concat(thirdPage).Select(run => run.Id).ToList();

            Assert.Equal(5, ids.Distinct().Count());
            Assert.Equal(ids.OrderByDescending(id => id, StringComparer.Ordinal), ids);
            Assert.True(firstPage[0].StartedAt >= thirdPage[0].StartedAt);
        }

        [RedisFact]
        public async Task The_Loop_Registers_The_Job_And_Marks_It_Stopped_When_It_Cannot_Schedule()
        {
            var runner = RunnerFor(new StatusBrokenIntervalCron());

            await runner.RunLoopAsync();

            var status = await CronStatus.GetAsync(runner.JobName);

            Assert.NotNull(status);
            Assert.Equal(CronStatus.KIND_INTERVAL, status.Kind);
            Assert.Null(status.Interval);
            Assert.NotNull(status.StoppedAt);
            Assert.Contains("failed to compute its next run", status.StoppedReason);
            Assert.Contains("no interval today", status.StoppedReason);

            // A restart clears the mark.
            await CronStatus.RegisterAsync(runner.JobName, CronStatus.KIND_INTERVAL, null, null, null);

            status = await CronStatus.GetAsync(runner.JobName);
            Assert.NotNull(status);
            Assert.Null(status.StoppedAt);
            Assert.Null(status.StoppedReason);
        }

        [RedisFact]
        public async Task The_Loop_Records_Its_Next_Run()
        {
            using var stopping = new CancellationTokenSource();
            var runner = new CronRunner(new StatusOneShotCron(), now => now + StatusOneShotCron.INTERVAL, stopping.Token, redisConfigured: false);
            var before = DateTime.UtcNow;

            var loop = runner.RunLoopAsync();
            await WaitUntilAsync(async () => (await CronStatus.GetAsync(runner.JobName))?.NextRunAt is not null);

            var status = await CronStatus.GetAsync(runner.JobName);

            Assert.NotNull(status);
            Assert.NotNull(status.NextRunAt);
            Assert.InRange(status.NextRunAt.Value, before + StatusOneShotCron.INTERVAL - TimeSpan.FromSeconds(1), DateTime.UtcNow + StatusOneShotCron.INTERVAL);

            await stopping.CancelAsync();
            await loop;
        }

        [RedisFact]
        public async Task List_Returns_Every_Registered_Job_Ordered_By_Name()
        {
            await CronStatus.RegisterAsync("ZebraJob", CronStatus.KIND_SCHEDULED, null, "0 * * * * *", null);
            await CronStatus.RegisterAsync("AlphaJob", CronStatus.KIND_INTERVAL, TimeSpan.FromMinutes(1), null, null);

            var statuses = await CronStatus.ListAsync();

            Assert.Equal(["AlphaJob", "ZebraJob"], statuses.Select(status => status.Name));
            Assert.Equal("0 * * * * *", statuses[1].Expression);
            Assert.Equal(TimeSpan.FromMinutes(1), statuses[0].Interval);
            Assert.Null(await CronStatus.GetAsync("NoSuchJob"));
            Assert.Empty(await CronStatus.RunsAsync("NoSuchJob"));
        }

        [Fact]
        public async Task Without_Redis_Reads_Are_Empty_And_Writes_Are_Skipped()
        {
            var connectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");

            try
            {
                Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", null);

                await CronStatus.RegisterAsync("UnrecordedJob", CronStatus.KIND_INTERVAL, TimeSpan.FromMinutes(1), null, null);
                await CronStatus.RecordStartAsync("UnrecordedJob", DateTime.UtcNow);

                Assert.Empty(await CronStatus.ListAsync());
                Assert.Null(await CronStatus.GetAsync("UnrecordedJob"));
                Assert.Empty(await CronStatus.RunsAsync("UnrecordedJob"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", connectionString);
            }
        }

        [Fact]
        public async Task Runs_Rejects_A_Page_Outside_The_Allowed_Size()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CronStatus.RunsAsync("AnyJob", count: 0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CronStatus.RunsAsync("AnyJob", count: CronStatus.MAX_RUNS_PAGE + 1));
        }

        private static CronRunner RunnerFor(Cron job, CancellationToken stopping = default)
        {
            return new CronRunner(job, now => now + TimeSpan.FromSeconds(30), stopping, redisConfigured: true);
        }

        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("condition was not met in time");
                }

                await Task.Delay(20);
            }
        }

        public sealed class StatusOkCron : Cron
        {
            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task Run(CancellationToken stopping) => Task.CompletedTask;
        }

        public sealed class StatusFailingCron : Cron
        {
            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task Run(CancellationToken stopping) => throw new InvalidOperationException("boom");
        }

        public sealed class StatusGatedCron : Cron
        {
            public static TaskCompletionSource Gate { get; private set; } = new();

            public static void Reset() => Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task Run(CancellationToken stopping) => Gate.Task;
        }

        public sealed class StatusFanOutCron : FanOutCron<int>
        {
            public static int ItemCount { get; set; }

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override int Concurrency => 2;

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(Enumerable.Range(1, ItemCount).ToList());

            public override Task Process(int item, CancellationToken stopping) => Task.CompletedTask;
        }

        public sealed class StatusThrowingEnumerateFanOutCron : FanOutCron<int>
        {
            public static bool Throw { get; set; }

            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task<List<int>> Enumerate(CancellationToken stopping)
            {
                return Throw ? throw new InvalidOperationException("enumerate broke") : Task.FromResult(new List<int> { 1, 2 });
            }

            public override Task Process(int item, CancellationToken stopping) => Task.CompletedTask;
        }

        public sealed class StatusPagedCron : Cron
        {
            public override TimeSpan Interval => TimeSpan.FromSeconds(30);

            public override Task Run(CancellationToken stopping) => Task.CompletedTask;
        }

        public sealed class StatusBrokenIntervalCron : Cron
        {
            public override TimeSpan Interval => throw new InvalidOperationException("no interval today");

            public override Task Run(CancellationToken stopping) => Task.CompletedTask;
        }

        public sealed class StatusOneShotCron : Cron
        {
            internal static readonly TimeSpan INTERVAL = TimeSpan.FromMinutes(5);

            public override TimeSpan Interval => INTERVAL;

            public override Task Run(CancellationToken stopping) => Task.CompletedTask;
        }
    }
}

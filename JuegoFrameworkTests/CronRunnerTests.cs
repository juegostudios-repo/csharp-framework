using JuegoFramework.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace JuegoFrameworkTests
{
    /// <summary>
    /// Redis free tests for the cron runner: crash safety, shutdown draining and job discovery.
    /// </summary>
    [Collection(CronCollection.NAME)]
    public class CronRunnerTests
    {
        private const int TEST_TIMEOUT_MS = 5000;

        [Fact]
        public async Task Run_That_Throws_Does_Not_Stop_The_Loop()
        {
            var job = new ThrowsOnFirstRunCron();

            CronJobService.StartJobs([job]);

            try
            {
                await WaitUntil(() => job.CallCount >= 2);
            }
            finally
            {
                await CronJobService.StopAsync();
            }

            Assert.True(job.CallCount >= 2, $"expected a second run, got {job.CallCount}");
        }

        [Fact]
        public async Task StopAsync_Waits_For_A_Job_That_Is_Mid_Run()
        {
            var job = new GatedCron();

            CronJobService.StartJobs([job]);

            try
            {
                await WaitUntil(() => job.Started.Task.IsCompleted);

                var stop = CronJobService.StopAsync();

                // The job is still inside Run, so the drain must not have finished.
                var finishedEarly = await Task.WhenAny(stop, Task.Delay(200)) == stop;
                Assert.False(finishedEarly, "StopAsync returned while the job was still running");
                Assert.False(job.Finished);

                job.Gate.SetResult();

                await stop.WaitAsync(TimeSpan.FromMilliseconds(TEST_TIMEOUT_MS));
                Assert.True(job.Finished);
            }
            finally
            {
                job.Gate.TrySetResult();
                await CronJobService.StopAsync();
            }
        }

        [Fact]
        public async Task A_Failing_Next_Tick_Ends_The_Loop_Without_Faulting_It()
        {
            var job = new BrokenIntervalCron();

            CronJobService.StartJobs([job]);

            try
            {
                var loop = Assert.Single(CronJobService.Loops);

                await WaitUntil(() => loop.IsCompleted);

                Assert.True(loop.IsCompletedSuccessfully, "the loop task faulted instead of stopping cleanly");
                Assert.False(job.WasRun);
            }
            finally
            {
                await CronJobService.StopAsync();
            }
        }

        [Fact]
        public async Task A_Non_Positive_Interval_Is_Not_Scheduled()
        {
            var job = new ZeroIntervalCron();

            CronJobService.StartJobs([job]);

            try
            {
                Assert.Empty(CronJobService.Loops);

                await Task.Delay(100);
                Assert.False(job.WasRun);
            }
            finally
            {
                await CronJobService.StopAsync();
            }
        }

        [Fact]
        public void Discovery_Constructs_A_Job_From_The_Service_Provider()
        {
            var counter = new Counter { Count = 42 };
            var services = new ServiceCollection().AddSingleton(counter).BuildServiceProvider();

            var jobs = CronJobService.DiscoverJobs(typeof(CronRunnerTests).Assembly, services);

            var job = Assert.Single(jobs.OfType<DependentCron>());
            Assert.Same(counter, job.Counter);
        }

        [Fact]
        public void Discovery_Names_A_Job_Whose_Dependency_The_Provider_Cannot_Supply()
        {
            var services = new ServiceCollection().BuildServiceProvider();

            var e = Assert.Throws<InvalidOperationException>(() => CronJobService.DiscoverJobs(typeof(CronRunnerTests).Assembly, services));

            Assert.Contains(nameof(DependentCron), e.Message);
        }

        [Fact]
        public void Discovery_Skips_Abstract_Types_And_Finds_Subclasses_Of_A_Generic_Base()
        {
            var services = new ServiceCollection().AddSingleton<Counter>().BuildServiceProvider();
            var jobs = CronJobService.DiscoverJobs(typeof(CronRunnerTests).Assembly, services);
            var types = jobs.Select(job => job.GetType()).ToList();

            Assert.DoesNotContain(typeof(AbstractIntermediateCron), types);
            Assert.Contains(typeof(ConcreteBelowAbstractCron), types);
            Assert.Contains(typeof(GenericIntermediateCron), types);
            Assert.DoesNotContain(types, type => type.IsAbstract);

            // An open generic cannot be instantiated; discovery must skip it rather than throw.
            Assert.DoesNotContain(types, type => type.IsGenericTypeDefinition);
            Assert.DoesNotContain(types, type => type.Name == typeof(OpenGenericCron<>).Name);
        }

        [Fact]
        public void Aligned_Ticks_Land_On_The_Next_Interval_Boundary()
        {
            var interval = TimeSpan.FromSeconds(30);
            var now = new DateTime(2026, 1, 1, 12, 0, 7, DateTimeKind.Utc);

            Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 30, DateTimeKind.Utc), CronRunner.AlignedNextTick(now, interval));

            // On a boundary the next slot is the one after it, so a runner never reclaims the slot
            // it has just finished.
            Assert.Equal(
                new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Utc),
                CronRunner.AlignedNextTick(new DateTime(2026, 1, 1, 12, 0, 30, DateTimeKind.Utc), interval));

            // Every container inside the same slot computes the same tick, which is what makes a
            // slot claim mean anything.
            Assert.Equal(
                CronRunner.AlignedNextTick(now, interval),
                CronRunner.AlignedNextTick(now.AddMilliseconds(12345), interval));
        }

        [Fact]
        public void Aligned_Ticks_Match_The_Unix_Second_Formula()
        {
            var interval = TimeSpan.FromSeconds(45);
            var now = new DateTime(2026, 3, 9, 4, 33, 17, DateTimeKind.Utc);

            var nowUnix = new DateTimeOffset(now).ToUnixTimeSeconds();
            var intervalSeconds = (long)interval.TotalSeconds;
            var expected = DateTimeOffset.FromUnixTimeSeconds(nowUnix / intervalSeconds * intervalSeconds + intervalSeconds).UtcDateTime;

            Assert.Equal(expected, CronRunner.AlignedNextTick(now, interval));
        }

        [Fact]
        public void Sleep_Is_Clamped_Between_MinSleep_And_The_Interval()
        {
            var interval = TimeSpan.FromSeconds(30);

            Assert.Equal(interval, CronRunner.ClampSleep(null, interval));
            Assert.Equal(CronRunner.MIN_SLEEP, CronRunner.ClampSleep(TimeSpan.Zero, interval));
            Assert.Equal(CronRunner.MIN_SLEEP, CronRunner.ClampSleep(TimeSpan.FromSeconds(-5), interval));
            Assert.Equal(interval, CronRunner.ClampSleep(TimeSpan.FromMinutes(5), interval));
            Assert.Equal(TimeSpan.FromSeconds(4), CronRunner.ClampSleep(TimeSpan.FromSeconds(4), interval));
        }

        [Fact]
        public void A_Job_That_Needs_Redis_Is_Named_When_There_Is_No_Connection_String()
        {
            var connectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");

            try
            {
                Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", null);

                // A fan out job claims its items in Redis, so it cannot start without one.
                Assert.Equal(nameof(IdleFanOut), CronJobService.FindJobRequiringRedis([new IdleFanOut()]));

                // A plain job only uses Redis when it is there, so a project without one keeps working.
                Assert.Null(CronJobService.FindJobRequiringRedis([new ZeroIntervalCron(), new ConcreteBelowAbstractCron()]));

                Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", "localhost:6379");
                Assert.Null(CronJobService.FindJobRequiringRedis([new IdleFanOut()]));
            }
            finally
            {
                Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", connectionString);
            }
        }

        [Fact]
        public void Two_Jobs_With_The_Same_Class_Name_Are_Reported()
        {
            Assert.Equal(nameof(ZeroIntervalCron), CronJobService.FindClashingName([new ZeroIntervalCron(), new Clash.ZeroIntervalCron()]));
            Assert.Null(CronJobService.FindClashingName([new ZeroIntervalCron(), new IdleFanOut()]));
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(TEST_TIMEOUT_MS);

            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("condition was not met in time");
                }

                await Task.Delay(10);
            }
        }

        public sealed class ThrowsOnFirstRunCron : Cron
        {
            public override TimeSpan Interval => TimeSpan.FromMilliseconds(20);

            private int _callCount;

            public int CallCount => Volatile.Read(ref _callCount);

            public override Task Run(CancellationToken stopping)
            {
                if (Interlocked.Increment(ref _callCount) == 1)
                {
                    throw new InvalidOperationException("first run fails on purpose");
                }

                return Task.CompletedTask;
            }
        }

        public sealed class GatedCron : Cron
        {
            public override TimeSpan Interval => TimeSpan.FromMilliseconds(20);

            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int _finished;

            public bool Finished => Volatile.Read(ref _finished) == 1;

            public override async Task Run(CancellationToken stopping)
            {
                Started.TrySetResult();
                await Gate.Task;
                Volatile.Write(ref _finished, 1);
            }
        }

        /// <summary>
        /// The first read of Interval succeeds so that the job passes startup validation; every later
        /// read, which is what the loop does, throws.
        /// </summary>
        public sealed class BrokenIntervalCron : Cron
        {
            private int _intervalReads;
            private int _wasRun;

            public override TimeSpan Interval =>
                Interlocked.Increment(ref _intervalReads) == 1
                    ? TimeSpan.FromMilliseconds(20)
                    : throw new InvalidOperationException("the interval is broken on purpose");

            public bool WasRun => Volatile.Read(ref _wasRun) == 1;

            public override Task Run(CancellationToken stopping)
            {
                Volatile.Write(ref _wasRun, 1);
                return Task.CompletedTask;
            }
        }

        public sealed class ZeroIntervalCron : Cron
        {
            private int _wasRun;

            public override TimeSpan Interval => TimeSpan.Zero;

            public bool WasRun => Volatile.Read(ref _wasRun) == 1;

            public override Task Run(CancellationToken stopping)
            {
                Volatile.Write(ref _wasRun, 1);
                return Task.CompletedTask;
            }
        }

        // A concrete open generic. Activator cannot instantiate it, so discovery has to skip it.
        public sealed class OpenGenericCron<T> : Cron
        {
            public override TimeSpan Interval => TimeSpan.FromHours(1);

            public override Task Run(CancellationToken stopping) => Task.CompletedTask;
        }

        // A never scheduled base, only here so discovery has an abstract intermediate to skip.
        public abstract class AbstractIntermediateCron : Cron
        {
            public override TimeSpan Interval => TimeSpan.FromHours(1);
        }

        public sealed class ConcreteBelowAbstractCron : AbstractIntermediateCron
        {
            public override Task Run(CancellationToken stopping) => Task.CompletedTask;
        }

        // Stands in for the FanOutCron<TItem> base a later task adds.
        public abstract class GenericBase<T> : Cron
        {
            public override TimeSpan Interval => TimeSpan.FromHours(1);

            public override Task Run(CancellationToken stopping) => Task.CompletedTask;
        }

        public sealed class GenericIntermediateCron : GenericBase<long>
        {
        }

        /// <summary>
        /// Has no parameterless constructor, so discovery can only build it through a provider.
        /// </summary>
        public sealed class DependentCron : Cron
        {
            public DependentCron(Counter counter)
            {
                Counter = counter;
            }

            public Counter Counter { get; }

            public override TimeSpan Interval => TimeSpan.FromHours(1);

            public override Task Run(CancellationToken stopping) => Task.CompletedTask;
        }

        public sealed class IdleFanOut : FanOutCron<int>
        {
            public override TimeSpan Interval => TimeSpan.FromHours(1);

            public override Task<List<int>> Enumerate(CancellationToken stopping) => Task.FromResult(new List<int>());

            public override Task Process(int item, CancellationToken stopping) => Task.CompletedTask;
        }
    }
}

namespace JuegoFrameworkTests.Clash
{
    // Same class name as CronRunnerTests.ZeroIntervalCron, in another namespace, so the two would
    // share every Redis key and wake. Startup must refuse the pair.
    public sealed class ZeroIntervalCron : JuegoFramework.Helpers.Cron
    {
        public override TimeSpan Interval => TimeSpan.FromHours(1);

        public override Task Run(CancellationToken stopping) => Task.CompletedTask;
    }
}

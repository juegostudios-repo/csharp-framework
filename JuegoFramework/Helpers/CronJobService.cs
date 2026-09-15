using System.Reflection;
using System.Runtime.InteropServices;

namespace JuegoFramework.Helpers
{
    public class CronJobService
    {
        private static readonly List<CronRunner> _runners = [];
        private static readonly List<Task> _loops = [];
        private static CancellationTokenSource? _stoppingSource;

        /// <summary>
        /// The loop task of every scheduled job. Exposed so tests can observe a loop that ended on its own.
        /// </summary>
        internal static IReadOnlyList<Task> Loops => _loops;

        public static async Task Start()
        {
            var cronJobs = DiscoverJobs(Assembly.GetEntryAssembly()!);

            if (cronJobs.Count == 0)
            {
                Log.Information("No cron jobs found");
                Environment.Exit(1);
            }

            var clashingName = FindClashingName(cronJobs);

            if (clashingName != null)
            {
                var clashingTypes = cronJobs.Select(job => job.GetType()).Where(type => type.Name == clashingName).Select(type => type.FullName);
                Log.Error("Two cron jobs are both named {Name} ({Types}). Redis keys and wakes are keyed on the bare class name, so rename one", clashingName, string.Join(", ", clashingTypes));
                Environment.Exit(1);
            }

            var jobNeedingRedis = FindJobRequiringRedis(cronJobs);

            if (jobNeedingRedis != null)
            {
                Log.Error("Cron {Name} needs Redis for its item claims, but REDIS_CONNECTION_STRING is empty", jobNeedingRedis);
                Environment.Exit(1);
            }

            if (!CronRedisKeys.IsConfigured)
            {
                Log.Warning("REDIS_CONNECTION_STRING is empty, so every cron job runs on this container's own timer. Run one CRON container only, or each job runs once per container");
            }

            var stoppingSource = StartJobs(cronJobs);

            // SIGTERM is what docker stop and Kubernetes send; SIGINT is Ctrl+C, on Windows too.
            // Cancelling the signal keeps the runtime from tearing the process down before the
            // drain has finished.
            using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnShutdownSignal);
            using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnShutdownSignal);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Shutdown was requested.
            }

            await StopAsync();

            // Start never returns: a consumer's Program.cs awaits Application.InitCron() and then
            // falls through to app.Run(), which would boot the web API inside the CRON container.
            Environment.Exit(0);
        }

        private static void OnShutdownSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            RequestStop();
        }

        private static void RequestStop()
        {
            Log.Information("Shutdown requested, stopping cron scheduling");
            _stoppingSource?.Cancel();
        }

        /// <summary>
        /// Finds every concrete cron job in the given assembly. The entry assembly in a test host is
        /// the test runner, so the assembly is a parameter rather than looked up here.
        /// </summary>
        internal static List<object> DiscoverJobs(Assembly assembly)
        {
            return [.. assembly.GetTypes()
                .Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition && IsCronJob(type))
                .Select(Construct)
                .OfType<object>()];
        }

        /// <summary>
        /// Constructs one job, naming it when its constructor throws. A ScheduledCron parses its
        /// Expression there, so a bad expression would otherwise surface as a bare
        /// TargetInvocationException with no job name in it.
        /// </summary>
        private static object? Construct(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                Log.Error(e.InnerException, "Cron {Name} could not be constructed", type.Name);
                throw new InvalidOperationException($"Cron {type.Name} could not be constructed", e.InnerException);
            }
        }

        /// <summary>
        /// Walks up the base type chain so that subclasses of an intermediate base, generic ones
        /// included, are found and not just direct subclasses of Cron and ScheduledCron.
        /// </summary>
        private static bool IsCronJob(Type type)
        {
            for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (baseType == typeof(Cron) || baseType == typeof(ScheduledCron))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a class name shared by two or more jobs, or null. Slot claims, item claims and
        /// wakes are all keyed on the bare class name, so two jobs sharing one would share those too.
        /// </summary>
        internal static string? FindClashingName(IEnumerable<object> cronJobs)
        {
            return cronJobs
                .GroupBy(job => job.GetType().Name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1)?
                .Key;
        }

        /// <summary>
        /// Returns the name of the first fan out job while REDIS_CONNECTION_STRING is empty, or null
        /// when every job can run. A fan out job claims its items in Redis and cannot work without
        /// it. A plain job uses Redis to agree on who runs each tick when it is there, and runs on
        /// its own timer when it is not, so a project without Redis keeps working.
        /// </summary>
        internal static string? FindJobRequiringRedis(IEnumerable<object> cronJobs)
        {
            if (CronRedisKeys.IsConfigured)
            {
                return null;
            }

            return cronJobs.FirstOrDefault(cronJob => cronJob is IFanOutJob)?.GetType().Name;
        }

        /// <summary>
        /// Starts one runner per job and returns the source that stops them all.
        /// </summary>
        internal static CancellationTokenSource StartJobs(IEnumerable<object> cronJobs)
        {
            var stoppingSource = new CancellationTokenSource();
            _stoppingSource = stoppingSource;
            _runners.Clear();
            _loops.Clear();

            // Runners register themselves with the wake channel, so a restart must not leave the
            // handlers of the previous set of jobs behind.
            CronWake.ClearHandlers();

            var redisConfigured = CronRedisKeys.IsConfigured;

            foreach (var cronJob in cronJobs)
            {
                CronRunner runner;

                if (cronJob is ScheduledCron scheduledCronJob)
                {
                    scheduledCronJob.SetStopping(stoppingSource.Token);
                    runner = new CronRunner(scheduledCronJob, scheduledCronJob.GetNextOccurrence, stoppingSource.Token, redisConfigured);
                }
                else if (cronJob is Cron regularCronJob)
                {
                    // System.Timers.Timer used to reject a non-positive interval at startup. Without
                    // this the loop would spin with no sleep between runs.
                    if (regularCronJob.Interval <= TimeSpan.Zero)
                    {
                        Log.Error("Cron {Name} has a non-positive interval of {Interval}, it will not be scheduled", regularCronJob.GetType().Name, regularCronJob.Interval);
                        continue;
                    }

                    regularCronJob.SetStopping(stoppingSource.Token);

                    // Without Redis the interval is measured from the end of the previous run, so the
                    // first run happens one interval after start. With Redis the runner aligns the
                    // ticks to the clock instead, so that every container claims the same slot.
                    runner = new CronRunner(regularCronJob, now => now + regularCronJob.Interval, stoppingSource.Token, redisConfigured);
                }
                else
                {
                    continue;
                }

                Log.Information($"Starting {cronJob.GetType().Name}");

                _runners.Add(runner);
                _loops.Add(Task.Run(runner.RunLoopAsync));
            }

            return stoppingSource;
        }

        /// <summary>
        /// Stops scheduling and waits for in-flight runs to finish.
        /// </summary>
        internal static async Task StopAsync()
        {
            var stoppingSource = _stoppingSource;

            if (stoppingSource == null)
            {
                return;
            }

            await stoppingSource.CancelAsync();

            var runningCount = _runners.Count(runner => runner.IsRunning);
            if (runningCount > 0)
            {
                Log.Information("Waiting for {Count} running jobs", runningCount);
            }

            try
            {
                await Task.WhenAll(_loops);
            }
            catch (Exception e)
            {
                // A loop can only fault outside Run, for instance on a job whose Interval throws.
                // The drain must still finish so that shutdown is not blocked by it.
                Log.Error(e, "A cron loop faulted while draining");
            }

            _runners.Clear();
            _loops.Clear();
            _stoppingSource = null;
            stoppingSource.Dispose();
        }
    }
}

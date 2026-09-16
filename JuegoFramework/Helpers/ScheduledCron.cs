using Cronos;

namespace JuegoFramework.Helpers
{
    /// <summary>
    /// Base class for a scheduled cron job. Implementers should provide the cron expression by
    /// overriding the Expression property and an asynchronous implementation of the Run method.
    /// <para>
    /// Each occurrence runs once across the fleet: with REDIS_CONNECTION_STRING set the occurrence
    /// is claimed in Redis, so one container runs it and the others skip, and an occurrence whose
    /// previous run is still in flight anywhere is skipped rather than doubled up. Without Redis
    /// the job runs on its own timer, which is the single container case.
    /// </para>
    /// <para>
    /// A job is constructed once, at worker start, through the application's service provider, so
    /// it can take its dependencies in its constructor. A scoped dependency must be resolved per run
    /// from an injected <c>IServiceScopeFactory</c>: the job itself lives for the whole process.
    /// </para>
    /// </summary>
    public abstract class ScheduledCron
    {
        private readonly CronExpression _cronExpression;

        /// <summary>
        /// The cron expression that defines the schedule for the job. Derived classes must define this.
        /// </summary>
        public abstract string Expression { get; }

        protected ScheduledCron()
        {
            _cronExpression = CronExpression.Parse(Expression, CronFormat.IncludeSeconds);
        }

        /// <summary>
        /// Runs the scheduled cron job. Implementers are expected to provide an
        /// asynchronous implementation.
        /// </summary>
        /// <param name="stopping">Cancelled when the process is shutting down. A long running job
        /// passes it to the work it awaits, or polls it, so that it can exit early.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public abstract Task Run(CancellationToken stopping);

        /// <summary>
        /// Gets the next occurrence of the cron job based on the cron expression.
        /// </summary>
        /// <returns>The next occurrence as a <see cref="DateTime"/>.</returns>
        internal DateTime? GetNextOccurrence()
        {
            return GetNextOccurrence(DateTime.UtcNow);
        }

        /// <summary>
        /// Gets the first occurrence of the cron job strictly after the given UTC time.
        /// </summary>
        internal DateTime? GetNextOccurrence(DateTime fromUtc)
        {
            return _cronExpression.GetNextOccurrence(fromUtc);
        }
    }
}

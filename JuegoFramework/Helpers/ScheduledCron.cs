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
        /// Cancelled when the process is shutting down. A long running job should pass this
        /// to the work it awaits, or poll it, so that it can exit early.
        /// </summary>
        protected CancellationToken Stopping { get; private set; }

        /// <summary>
        /// Set by the cron runner before the job is scheduled.
        /// </summary>
        internal void SetStopping(CancellationToken stopping) => Stopping = stopping;

        /// <summary>
        /// Runs the scheduled cron job. Implementers are expected to provide an
        /// asynchronous implementation.
        /// </summary>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public abstract Task Run();

        /// <summary>
        /// Gets the next occurrence of the cron job based on the cron expression.
        /// </summary>
        /// <returns>The next occurrence as a <see cref="DateTime"/>.</returns>
        public DateTime? GetNextOccurrence()
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

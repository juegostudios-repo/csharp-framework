using Cronos;

namespace JuegoFramework.Helpers
{
    /// <summary>
    /// Base class for a scheduled cron job. Implementers should provide the cron expression by
    /// overriding the Expression property and an asynchronous implementation of the Run method.
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
        /// <returns>A Task representing the asynchronous operation.</returns>
        public abstract Task Run();

        /// <summary>
        /// Gets the next occurrence of the cron job based on the cron expression.
        /// </summary>
        /// <returns>The next occurrence as a <see cref="DateTime"/>.</returns>
        public DateTime? GetNextOccurrence()
        {
            return _cronExpression.GetNextOccurrence(DateTime.UtcNow);
        }

        public async Task RunScheduledTask()
        {
            while (true)
            {
                var nextOccurrence = GetNextOccurrence();
                if (nextOccurrence.HasValue)
                {
                    Log.Information($"{GetType().Name} next occurrence: {nextOccurrence.Value}");
                    var delay = nextOccurrence.Value - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay);
                    }
                    await Run();
                }
                else
                {
                    throw new InvalidOperationException("Invalid cron expression.");
                }
            }
        }
    }
}

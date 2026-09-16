namespace JuegoFramework.Helpers
{
    /// <summary>
    /// Base class for a cron job. Implementers should provide the interval by
    /// overriding the Interval property and an asynchronous implementation of the Run method.
    /// <para>
    /// A cron job runs once per tick, wherever the fleet happens to run it. With
    /// REDIS_CONNECTION_STRING set, ticks are aligned to the clock
    /// (<c>floor(nowUnix / Interval) * Interval + Interval</c>) and each tick is claimed in Redis,
    /// so one container runs it and the others skip. A tick whose previous run is still in flight
    /// anywhere in the fleet is skipped rather than doubled up. Without Redis the job runs on its
    /// own timer, which is the single container case.
    /// </para>
    /// <para>
    /// A job is constructed once, at worker start, through the application's service provider, so
    /// it can take its dependencies in its constructor. A scoped dependency must be resolved per run
    /// from an injected <c>IServiceScopeFactory</c>: the job itself lives for the whole process.
    /// </para>
    /// </summary>
    public abstract class Cron
    {
        /// <summary>
        /// The interval at which the cron job should run. Derived classes must define this.
        /// </summary>
        public abstract TimeSpan Interval { get; }

        /// <summary>
        /// Runs the cron job. Implementers are expected to provide an
        /// asynchronous implementation.
        /// </summary>
        /// <param name="stopping">Cancelled when the process is shutting down. A long running job
        /// passes it to the work it awaits, or polls it, so that it can exit early.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public abstract Task Run(CancellationToken stopping);
    }
}

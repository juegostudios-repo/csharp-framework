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
    /// </summary>
    public abstract class Cron
    {
        /// <summary>
        /// The interval at which the cron job should run. Derived classes must define this.
        /// </summary>
        public abstract TimeSpan Interval { get; }

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
        /// Runs the cron job. Implementers are expected to provide an
        /// asynchronous implementation.
        /// </summary>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public abstract Task Run();
    }
}

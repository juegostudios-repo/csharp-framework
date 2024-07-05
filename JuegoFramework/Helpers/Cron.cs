namespace JuegoFramework.Helpers
{
    /// <summary>
    /// Base class for a cron job. Implementers should provide the interval by
    /// overriding the Interval property and an asynchronous implementation of the Run method.
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
        /// <returns>A Task representing the asynchronous operation.</returns>
        public abstract Task Run();
    }
}

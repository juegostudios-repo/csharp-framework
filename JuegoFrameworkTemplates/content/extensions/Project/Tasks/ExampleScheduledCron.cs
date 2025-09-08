using JuegoFramework.Helpers;

namespace API.Tasks
{
    public class ExampleScheduledCron : ScheduledCron
    {
        public override string Expression => "0/5 * * * * ?"; // Every 5 seconds
        private readonly Serilog.ILogger _logger;

        public ExampleScheduledCron()
        {
            _logger = Log.ForContext("CronName", GetType().Name);
        }

        public override Task Run()
        {
            try
            {
                _logger.Information("=====Ping=====");
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error");
            }
            return Task.CompletedTask;
        }
    }
}

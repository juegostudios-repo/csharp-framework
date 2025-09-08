using JuegoFramework.Helpers;

namespace API.Tasks
{
    public class ExampleCron : Cron
    {
        public override TimeSpan Interval => TimeSpan.FromSeconds(5);
        private readonly Serilog.ILogger _logger;

        public ExampleCron()
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

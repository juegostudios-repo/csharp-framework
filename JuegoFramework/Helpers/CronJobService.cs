using System.Reflection;

namespace JuegoFramework.Helpers
{
    public class CronJobService
    {
        public static async Task Start()
        {
            // includes all the cron jobs that implement Cron abstract class
            var cronJobs = Assembly.GetEntryAssembly()!.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Cron)))
                .Select(Activator.CreateInstance)
                .Cast<Cron>()
                .ToList();

            if (cronJobs.Count == 0)
            {
                Log.Information("No cron jobs found");
                Environment.Exit(1);
            }

            // create a SingleExecutionAsyncTimer for each cron job
            foreach (var cronJob in cronJobs)
            {
                Log.Information($"Starting {cronJob.GetType().Name}");
                var timer = new SingleExecutionAsyncTimer(cronJob.Run, cronJob.Interval);
            }

            await Task.Delay(-1);
        }
    }
}

using System.Reflection;

namespace JuegoFramework.Helpers
{
    public class CronJobService
    {
        public static async Task Start()
        {
            // includes all the cron jobs that implement Cron abstract class
            var cronJobs = Assembly.GetEntryAssembly()!.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Cron)) || t.IsSubclassOf(typeof(ScheduledCron)))
                .Select(Activator.CreateInstance)
                .ToList();

            if (cronJobs.Count == 0)
            {
                Log.Information("No cron jobs found");
                Environment.Exit(1);
            }

            // create a SingleExecutionAsyncTimer for each cron job
            foreach (var cronJob in cronJobs)
            {
                if (cronJob == null)
                {
                    continue;
                }

                Log.Information($"Starting {cronJob.GetType().Name}");
                if (cronJob is ScheduledCron scheduledCronJob)
                {
                    _ = Task.Run(scheduledCronJob.RunScheduledTask);
                }
                else if (cronJob is Cron regularCronJob)
                {
                    var timer = new SingleExecutionAsyncTimer(regularCronJob.Run, regularCronJob.Interval);
                }
            }

            await Task.Delay(-1);
        }
    }
}

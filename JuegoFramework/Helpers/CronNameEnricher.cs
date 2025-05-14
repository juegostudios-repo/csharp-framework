using Serilog.Core;
using Serilog.Events;

public class CronNameEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.TryGetValue("CronName", out var cronName))
        {
            var formattedCronName = " " + cronName.ToString().Trim('"');
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("FormattedCronName", formattedCronName));
        }
    }
}
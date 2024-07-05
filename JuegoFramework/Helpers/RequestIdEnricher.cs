using Serilog.Core;
using Serilog.Events;
using System.Security.Cryptography;
using System.Text;

namespace JuegoFramework.Helpers;

public class RequestIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.TryGetValue("RequestId", out LogEventPropertyValue? requestIdValue))
        {
            var requestIdText = requestIdValue.ToString().Trim('"');

            if (!string.IsNullOrWhiteSpace(requestIdText))
            {
                // Compute hash of the original request ID
                using (var sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(requestIdText));

                    // Convert the hash bytes to a shorter string representation
                    string shortRequestId = Convert.ToBase64String(hashBytes)
                        .Replace("/", "")
                        .Replace("+", "")
                        .Replace("=", "")
                        .Substring(0, 8);

                    var formattedRequestId = shortRequestId + " ";
                    var formattedRequestIdProperty = new LogEventProperty("RequestId", new ScalarValue(formattedRequestId));
                    logEvent.AddOrUpdateProperty(formattedRequestIdProperty);
                }
            }
        }
    }
}

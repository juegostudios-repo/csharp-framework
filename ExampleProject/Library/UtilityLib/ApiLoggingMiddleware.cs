using System.Text;
using System.Text.Json;
using API.Models;

namespace API.Library;

public class ApiLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public ApiLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var originalRequestBodyStream = context.Request.Body;
        var originalResponseBodyStream = context.Response.Body;

        var accessToken = context.Request.Headers.TryGetValue("access_token", out var authorization) ? authorization.ToString() : "";
        User? user = null;

        if (accessToken != "")
        {
            user = await UserLib.FindOne(new {
                access_token = accessToken
            });
        }

        var requestBodyContent = await ReadRequestBody(context);

        var apiLogEntry = new ApiLog
        {
            UserId = user?.UserId,
            Method = context.Request.Method,
            Path = context.Request.Path,
            Request = JsonSerializer.Serialize(new
            {
                headers = context.Request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value!)),
                body = requestBodyContent != "" ? JsonSerializer.Deserialize<object>(requestBodyContent, _jsonSerializerOptions) : null,
            }, _jsonSerializerOptions),
            Response = "",
            CreatedAt = DateTime.UtcNow
        };

        long apiLogId = await ApiLogLib.Insert(apiLogEntry);

        using (var newResponseBodyStream = new MemoryStream())
        {
            context.Response.Body = newResponseBodyStream;

            await _next(context);

            string responseBodyContent = await ReadResponseBody(newResponseBodyStream);
            object deserizedResponseBody;

            try
            {
                deserizedResponseBody = JsonSerializer.Deserialize<object>(responseBodyContent, _jsonSerializerOptions) ?? responseBodyContent;
            }
            catch (JsonException)
            {
                deserizedResponseBody = responseBodyContent;
            }

            await ApiLogLib.Update(new {
                api_log_id = apiLogId,
            }, new {
                response = JsonSerializer.Serialize(new
                {
                    statusCode = context.Response.StatusCode,
                    headers = context.Response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value!)),
                    body = deserizedResponseBody,
                }, _jsonSerializerOptions)
            });

            await newResponseBodyStream.CopyToAsync(originalResponseBodyStream);
            context.Response.Body = originalResponseBodyStream;
        }

        context.Request.Body = originalRequestBodyStream;
    }

    private async Task<string> ReadRequestBody(HttpContext context)
    {
        context.Request.EnableBuffering();

        var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 1024, true);
        string body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        return body;
    }

    private async Task<string> ReadResponseBody(MemoryStream responseBodyStream)
    {
        responseBodyStream.Position = 0;
        var reader = new StreamReader(responseBodyStream);
        string text = await reader.ReadToEndAsync();
        responseBodyStream.Position = 0;
        return text;
    }
}

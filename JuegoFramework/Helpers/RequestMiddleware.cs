using System.Text;
using System.Text.Json;

namespace JuegoFramework.Helpers
{
    public class RequestMiddleware(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            if ((context.Request.Method == "POST" || context.Request.Method == "GET") && (context.Request.ContentType == "application/x-www-form-urlencoded" || context.Request.ContentType == "application/json"))
            {
                if (context.Request.ContentType == "application/x-www-form-urlencoded")
                {
                    // Read form data directly
                    var form = await context.Request.ReadFormAsync();
                    var formData = form.ToDictionary(x => x.Key, x => x.Value.ToString());

                    // Convert form data to JSON
                    var jsonFormData = JsonSerializer.Serialize(formData);

                    // Replace the request body with JSON data
                    var jsonBytes = Encoding.UTF8.GetBytes(jsonFormData);
                    context.Request.Body = new MemoryStream(jsonBytes);
                    context.Request.ContentType = "application/json";
                }
            }
            await _next(context);
        }
    }
}

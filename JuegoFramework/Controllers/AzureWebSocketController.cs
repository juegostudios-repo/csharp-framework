using Microsoft.AspNetCore.Mvc;
using JuegoFramework.Helpers;
using System.Text.Json;

namespace JuegoFramework.Controllers
{
    [Route("/azure-websocket")]
    [ApiController]
    public class AzureWebSocketController : ControllerBase
    {
        [HttpOptions]
        public IActionResult Options()
        {
            var origin = Request.Headers["WebHook-Request-Origin"].ToString();
            if (!string.IsNullOrEmpty(origin) && origin.EndsWith(".webpubsub.azure.com"))
            {
                Response.Headers.Append("WebHook-Allowed-Origin", origin);
                return Ok();
            }
            return BadRequest();
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromQuery] string @event)
        {
            var webSocketHandlerService = HttpContext.RequestServices.GetService(typeof(IWebSocketHandler)) as IWebSocketHandler ?? throw new InvalidOperationException("Unable to find websocket handler service.");

            if (string.IsNullOrEmpty(@event))
            {
                return BadRequest("Event parameter is required.");
            }

            var userid = Request.Headers["ce-userid"].ToString();
            var connectionId = Request.Headers["ce-connectionid"].ToString();
            var eventName = Request.Headers["ce-eventname"].ToString();
            string body;

            using (var reader = new StreamReader(Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }

            Log.Information($"Received event: {@event}");
            Log.Information($"Event headers: {JsonSerializer.Serialize(Request.Headers)}");
            Log.Information($"Event body: {body}");

            switch (@event.ToUpper())
            {
                case "CONNECT":
                    await webSocketHandlerService.ConnectSocket(userid, connectionId);
                    break;
                case "MESSAGE":
                    var response = await WebSocketHelper.HandleSocketMessage(body);
                    if (response != null && !string.IsNullOrWhiteSpace(response.RequestId))
                    {
                        await WebSocketService.SendMessageToSocket(connectionId, response);
                    }
                    break;
                case "DISCONNECTED":
                    await webSocketHandlerService.DisconnectSocket(connectionId);
                    break;
                default:
                    Log.Warning($"Unhandled event type: {@event}");
                    return BadRequest($"Unhandled event type: {@event}");
            }

            return Ok();
        }
    }
}

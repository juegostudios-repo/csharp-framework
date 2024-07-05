using Microsoft.AspNetCore.Mvc;
using JuegoFramework.Helpers;
using System.Text.Json;
using JuegoFramework.Controllers.Dto;

namespace JuegoFramework.Controllers
{
    [Route("/aws-websocket")]
    [ApiController]
    [Response]
    public class AWSWebSocketController() : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Post(AwsWebSocketRequestDto request)
        {
            var webSocketHandlerService = HttpContext.RequestServices.GetService(typeof(IWebSocketHandler)) as IWebSocketHandler ?? throw new InvalidOperationException("Unable to find websocket handler service.");

            Log.Information("Received WebSocket request: " + JsonSerializer.Serialize(request));

            if (request.EventType == "CONNECT")
            {
                await webSocketHandlerService.ConnectSocket(request.AccessToken, request.ConnectionId);
            }
            else if (request.EventType == "MESSAGE")
            {
                await WebSocketHelper.HandleSocketMessage(request.Body);
            }
            else if (request.EventType == "DISCONNECT")
            {
                await webSocketHandlerService.DisconnectSocket(request.ConnectionId);
            }

            return Ok();
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using JuegoFramework.Helpers;
using JuegoFramework.Controllers.Dto;

namespace JuegoFramework.Controllers
{
    [Route("/api-websocket")]
    [ApiController]
    public class ApiWebSocketController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Post(APIWebSocketRequestDto request)
        {
            await WebSocketService.SendMessageToSocket(request.ConnectionId, request.Message, true);
            return Ok();
        }
    }
}

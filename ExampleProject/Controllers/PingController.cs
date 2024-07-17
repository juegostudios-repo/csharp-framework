using Microsoft.AspNetCore.Mvc;
using JuegoFramework.Helpers;
using System.Reflection;

namespace API.Controllers
{
    [Route("/ping")]
    [ApiController]
    [Response]
    public class PingController : ControllerBase
    {
        [HttpGet]
        public IActionResult Ping()
        {
            return ApiResponse.setResponse("SUCCESS", new
            {
                curTime = DateTime.Now.ToString("o"),
                project = Assembly.GetExecutingAssembly().GetName().Name ?? "project"
            });
        }
    }
}

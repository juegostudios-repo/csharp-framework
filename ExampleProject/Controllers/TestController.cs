using Microsoft.AspNetCore.Mvc;
using JuegoFramework.Helpers;
using API.Models;

namespace API.Controllers
{
    [Route("/query-params-test")]
    [ApiController]
    [Response]
    public class TestController : ControllerBase
    {
        [HttpGet]
        public IActionResult QueryParamsTest(QueryParamsTestDto dto)
        {
            return ApiResponse.setResponse("SUCCESS", new
            {
                page = dto.Page,
                limit = dto.Limit
            });
        }
    }
}

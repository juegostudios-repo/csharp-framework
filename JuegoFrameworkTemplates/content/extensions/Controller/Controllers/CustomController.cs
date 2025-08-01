using Microsoft.AspNetCore.Mvc;
using JuegoFramework.Helpers;
#if IsAuthEnabled || IsDtoEnabled
using ProjectName.Models;
#endif

namespace ProjectName.Controllers
{
    [Route("custom")]
    [ApiController]
    [Response]
    public class CustomController : ControllerBase
    {
        [HttpGet]
#if IsAuthEnabled
        [UserAuth]
#endif
        [Consumes("application/x-www-form-urlencoded", "application/json")]
#if IsDtoEnabled
        public async Task<IActionResult> Get(CustomGetDto data)
#else
        public async Task<IActionResult> Get()
#endif
        {
            try
            {
#if IsAuthEnabled
                if (HttpContext.Items["User"] is not User userObj)
                {
                    return ApiResponse.setResponse("INVALID_INPUT_EMPTY", responseOption: "access_token");
                }
#endif
                // Write your logic here
                var response = new { };
                return ApiResponse.setResponse("SUCCESS", response);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in CustomController.Get");
                return ApiResponse.setResponse("UNKNOWN_ERROR");
            }
        }

        [HttpPost]
#if IsAuthEnabled
        [UserAuth]
#endif
        [Consumes("application/x-www-form-urlencoded", "application/json")]
#if IsDtoEnabled
        public async Task<IActionResult> Post(CustomPostDto data)
#else
        public async Task<IActionResult> Post()
#endif
        {
            try
            {
#if IsAuthEnabled
                if (HttpContext.Items["User"] is not User userObj)
                {
                    return ApiResponse.setResponse("INVALID_INPUT_EMPTY", responseOption: "access_token");
                }
#endif
                // Write your logic here
                return ApiResponse.setResponse("SUCCESS", new { });
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in CustomController.Post");
                return ApiResponse.setResponse("UNKNOWN_ERROR");
            }
        }
    }
}
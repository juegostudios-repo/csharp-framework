using Microsoft.AspNetCore.Mvc;
using JuegoFramework.Helpers;
using ProjectName.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;

namespace ProjectName.Controllers
{
    [Route("/user")]
    [ApiController]
    [Response]
    public class UserController : ControllerBase
    {
        private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

        [HttpPost("login")]
        [Consumes("application/x-www-form-urlencoded", "application/json")]
        public async Task<IActionResult> Login(LoginDto data)
        {
            try
            {
                await Task.CompletedTask;
                string accessToken = JwtHelper.GenerateJwtToken(data.UserName);
                return ApiResponse.setResponse("SUCCESS", new { access_token = accessToken });
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in UserController.Login");
                return ApiResponse.setResponse("INTERNAL_SERVER_ERROR");
            }
        }

        [HttpPost("logout")]
        [UserAuth]
        [Consumes("application/x-www-form-urlencoded", "application/json")]
        public async Task<IActionResult> Logout()
        {
            try
            {
                await Task.CompletedTask;
                return ApiResponse.setResponse("SUCCESS", new { });
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in UserController.Logout");
                return ApiResponse.setResponse("INTERNAL_SERVER_ERROR");
            }
        }


    }
}

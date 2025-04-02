using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace JuegoFramework.Helpers
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class UserAuth : Attribute, IAsyncAuthorizationFilter
    {

        private readonly ILoginService? _loginService;

        public UserAuth(ILoginService loginService)
        {
            _loginService = loginService;
        }

        public UserAuth()
        {
            _loginService = null;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.HttpContext.Request.Headers["access_token"] == StringValues.Empty)
            {
                context.Result = FormatResponse(context, ApiResponse.setResponse("INVALID_ACCESS_TOKEN"));
                return;
            }

            var authData = JwtHelper.ValidateJwtToken(context.HttpContext.Request.Headers["access_token"].ToString() ?? throw new ArgumentNullException("access_token", "The access_token is not set in the request headers."));

            if (authData == null)
            {
                context.Result = FormatResponse(context, ApiResponse.setResponse("INVALID_ACCESS_TOKEN"));
                return;
            }

            if (Global.Configuration!["AUTH:AUTH_MODE"] == "JWT_SQL")
            {
                var loginService = _loginService != null ? _loginService : context.HttpContext.RequestServices.GetService(typeof(ILoginService)) as ILoginService;

                if (loginService == null)
                {
                    throw new InvalidOperationException("Unable to find user login service.");
                }

                var userInDb = await loginService.ValidateAuthData(authData);

                if (userInDb == null)
                {
                    context.Result = FormatResponse(context, ApiResponse.setResponse("INVALID_ACCESS_TOKEN"));
                    return;
                }

                context.HttpContext.Items["User"] = userInDb;
            }
        }

        private static IActionResult FormatResponse(AuthorizationFilterContext context, IActionResult result)
        {
            if (result is OkObjectResult objectResult)
            {
                if (objectResult.Value is ReturnResponse returnResponse)
                {
                    var response = new ReturnResponse
                    {
                        responseStatus = returnResponse.responseStatus ?? "SUCCESS",
                        responseData = returnResponse.responseData ?? new { },
                        responseOption = returnResponse.responseOption ?? null
                    };

                    var apiResponse = new ApiResponse(context.HttpContext.Request.Headers.ContainsKey("AcceptLanguage") ? context.HttpContext.Request.Headers?.AcceptLanguage.First()?[..2] : null);
                    return apiResponse.GetResponse(response.responseStatus, response.responseData, response.responseOption);
                }
            }

            return result;
        }
    }
}

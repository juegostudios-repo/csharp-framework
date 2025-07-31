using Microsoft.AspNetCore.Mvc;

namespace JuegoFramework.Helpers
{
    public static class WebSocketResponseHelper
    {
        public static IActionResult FormatResponse(IActionResult actionResult, string? localizer = null)
        {
            if (actionResult is OkObjectResult objectResult)
            {
                if (objectResult.Value is ReturnResponse returnResponse)
                {
                    var response = new ReturnResponse
                    {
                        responseStatus = returnResponse.responseStatus ?? "SUCCESS",
                        responseData = returnResponse.responseData ?? new { },
                        responseOption = returnResponse.responseOption ?? null
                    };

                    var apiResponse = new ApiResponse(localizer);
                    return apiResponse.GetResponse(response.responseStatus, response.responseData, response.responseOption);
                }
            }

            // Fallback for other result types
            return actionResult;
        }
    }
}
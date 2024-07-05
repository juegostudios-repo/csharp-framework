using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JuegoFramework.Helpers
{
    public class ApiResponse(string? localizer = null)
    {
        public string? ResponseString { get; private set; }
        public int ResponseCode { get; private set; }
        public string? ResponseMessage { get; private set; }
        public object? ResponseData { get; private set; }
        public string? LngKey { get; private set; } = localizer;
        public const string DEFAULT_LNG_KEY = "en"; //  default language key.

        private static Dictionary<string, ResponseJson> baseResponse = Global.BaseResponse!;

        public static IActionResult setResponse(string responseStatus, object? responseData = null, string? responseOption = null)
        {
            SendResponse response = new();
            return response.setResponse(responseStatus, responseData ?? new { }, responseOption);
        }

        public IActionResult GetResponse(string responseString, object responseData, string? responseParam = null)
        {

            if (!string.IsNullOrEmpty(responseString))
            {
                ResponseString = responseString;
            }
            else
            {
                throw new Exception("responseString is required");
            }

            var responses = new Dictionary<string, ResponseJson>(baseResponse);

            if (!responses.ContainsKey(ResponseString))
            {
                responses = new Dictionary<string, ResponseJson> { ["UNKNOWN_ERROR"] = responses["UNKNOWN_ERROR"] };
            }
            else
            {
                responses = new Dictionary<string, ResponseJson> { [ResponseString] = responses[ResponseString] };
            }

            ResponseCode = responses.First().Value.ResponseCode;
            ResponseMessage = LngKey != null && responses.First().Value.ResponseMessage.ContainsKey(LngKey)
                ? responses.First().Value.ResponseMessage[LngKey]
                : responses.First().Value.ResponseMessage[DEFAULT_LNG_KEY];

            if (responseParam is not null)
            {
                ResponseMessage = ResponseMessage.Replace("paramName", responseParam);
            }

            SendResponse response = new();
            return response.getResponse(ResponseCode, ResponseMessage, responseData);
        }
    }


    public class ResponseAttribute : ActionFilterAttribute
    {
        public ApiResponse? apiResponse { get; set; }
        public IActionResult? actionResult { get; set; }

        public override void OnResultExecuting(ResultExecutingContext context)
        {
            base.OnResultExecuting(context);
            actionResult = context.Result;

            if (context.Result is OkObjectResult objectResult)
            {
                if (objectResult.Value is ReturnResponse returnResponse)
                {
                    var response = new ReturnResponse
                    {
                        responseStatus = returnResponse.responseStatus ?? "SUCCESS",
                        responseData = returnResponse.responseData ?? new { },
                        responseOption = returnResponse.responseOption ?? null
                    };

                    apiResponse = new ApiResponse(context.HttpContext.Request.Headers.ContainsKey("AcceptLanguage") ? context.HttpContext.Request.Headers?.AcceptLanguage.First()?[..2] : null);
                    context.Result = apiResponse.GetResponse(response.responseStatus, response.responseData, response.responseOption);
                }
            }

        }
    }

    public class ReturnResponse
    {
        public required string responseStatus { get; set; }
        public object responseData { get; set; } = new { };
        public string? responseOption { get; set; }
    }

    public class SendResponse : ControllerBase
    {
        public IActionResult setResponse(string responseStatus, object responseData, string? responseOption = null)
        {
            var response = new ReturnResponse
            {
                responseStatus = responseStatus,
                responseData = responseData,
                responseOption = responseOption
            };
            return Ok(response);
        }

        public IActionResult getResponse(int responseCode, string responseMessage, object responseData)
        {
            var response = new ApiReturnResponse
            {
                ResponseCode = responseCode,
                ResponseMessage = responseMessage,
                ResponseData = responseData
            };
            return Ok(response);
        }
    }

    public class ApiReturnResponse
    {
        public int ResponseCode { get; set; }
        public required string ResponseMessage { get; set; }
        public object? ResponseData { get; set; }
    }

    public class ResponseJson
    {
        [JsonPropertyName("responseCode")]
        public int ResponseCode { get; set; }

        [JsonPropertyName("responseMessage")]
        public required Dictionary<string, string> ResponseMessage { get; set; }
    }
}

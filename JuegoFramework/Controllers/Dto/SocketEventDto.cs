using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace JuegoFramework.Controllers.Dto
{
    public class SocketEventHeadersDto
    {
        [Required]
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; set; }
    }

    public class SocketEventDto
    {
        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        [Required]
        [JsonPropertyName("method")]
        public required string Method { get; set; }

        [Required]
        [JsonPropertyName("action")]
        public required string Action { get; set; }

        [Required]
        [JsonPropertyName("headers")]
        public required SocketEventHeadersDto Headers { get; set; }

        [Required]
        [JsonPropertyName("body")]
        public required dynamic Body { get; set; }
    }

    public class SocketEventResponseDto
    {
        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        [JsonPropertyName("body")]
        public object? Body { get; set; }
    }
}

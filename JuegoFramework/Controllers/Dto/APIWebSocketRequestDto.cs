using System.Text.Json.Serialization;

namespace JuegoFramework.Controllers.Dto
{
    public class APIWebSocketRequestDto
    {
        [JsonPropertyName("connection_id")]
        public required string ConnectionId { get; set; }

        [JsonPropertyName("message")]
        public required string Message { get; set; }
    }
}

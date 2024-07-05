using System.Text.Json.Serialization;

namespace JuegoFramework.Controllers.Dto
{
    public class AwsWebSocketRequestDto
    {
        [JsonPropertyName("event_type")]
        public required string EventType { get; set; }

        [JsonPropertyName("connection_id")]
        public required string ConnectionId { get; set; }

        [JsonPropertyName("access_token")]
        public required string AccessToken { get; set; }

        [JsonPropertyName("body")]
        public required string Body { get; set; }
    }
}

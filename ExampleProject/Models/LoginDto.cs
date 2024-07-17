using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace API.Models
{
    public class LoginDto
    {
        [Required]
        [JsonPropertyName("type")]
        public required short Type { get; set; }

        [JsonPropertyName("user_name")]
        public string? UserName { get; set; }
    }
}

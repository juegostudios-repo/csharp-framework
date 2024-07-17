using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace API.Models
{
    public class LoginDto
    {
        [Required]
        [JsonPropertyName("type")]
        public required short Type { get; set; }

        [Required]
        [JsonPropertyName("user_name")]
        public required string UserName { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ProjectName.Models
{
    public class CustomPostDto
    {
        [Required]
        [JsonPropertyName("inp_vals")]
        public required string InpVals { get; set; }
    }
}

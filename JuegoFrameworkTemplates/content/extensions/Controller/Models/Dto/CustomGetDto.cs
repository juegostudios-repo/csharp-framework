using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace ProjectName.Models
{
    public class CustomGetDto
    {
        [Required]
        [FromQuery(Name = "inp_vals")]
        public required string InpVals { get; set; }
    }
}

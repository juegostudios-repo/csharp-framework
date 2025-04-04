using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace ProjectName.Models;

public class QueryParamsTestDto
{
    [Required]
    [FromQuery(Name = "page")]
    public int Page { get; set; }

    [Required]
    [FromQuery(Name = "limit")]
    public int Limit { get; set; }
}

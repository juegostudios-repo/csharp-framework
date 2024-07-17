using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Models
{
    [Table("api_log")]
    public class ApiLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("api_log_id")]
        public int ApiLogId { get; set; }

        [Column("user_id")]
        public long? UserId { get; set; }

        [Required]
        [StringLength(50)]
        [Column("method")]
        public required string Method { get; set; }

        [Required]
        [StringLength(255)]
        [Column("path")]
        public required string Path { get; set; }

        [Required]
        [Column("request", TypeName = "TEXT")]
        public required string Request { get; set; }

        [Required]
        [Column("response", TypeName = "TEXT")]
        public required string Response { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("created_at", TypeName = "TIMESTAMP")]
        public DateTime CreatedAt { get; set; }
    }
}

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectName.Models
{
    [Table("user")]
    [Index(nameof(ConnectionId))]
    [Index(nameof(Status))]
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("user_id")]
        public long UserId { get; set; }

        [StringLength(255)]
        [Column("access_token")]
        public string? AccessToken { get; set; }

        [Required]
        [StringLength(255, MinimumLength = 1)]
        [Column("user_name")]
        public required string UserName { get; set; }

        [StringLength(255)]
        [Column("connection_id")]
        public string ConnectionId { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        [Column("device_id")]
        public required string DeviceId { get; set; }

        [Required]
        [Column("status")]
        [DefaultValue(2)]
        public short Status { get; set; } = 2;

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("created_at", TypeName = "TIMESTAMP")]
        public DateTime CreatedAt { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("updated_at", TypeName = "TIMESTAMP")]
        public DateTime UpdatedAt { get; set; }
    }
}

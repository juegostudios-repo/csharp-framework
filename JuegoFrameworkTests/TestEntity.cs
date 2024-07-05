using System.ComponentModel.DataAnnotations.Schema;

namespace JuegoFrameworkTests;

[Table("test_entities")]
public class TestEntity
{
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public required string Name { get; set; }

    [Column("json_data", TypeName = "JSON")]
    public required double[] JsonData { get; set; }

    [Column("counter")]
    public int Counter { get; set; }
}

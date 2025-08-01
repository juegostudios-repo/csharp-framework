using Dapper;
using MySqlConnector;
using System.Data;

public class MySqlDateTimeHandler : SqlMapper.TypeHandler<DateTime?>
{
    public override void SetValue(IDbDataParameter parameter, DateTime? value)
    {
        parameter.Value = value ?? (object)DBNull.Value;
    }

    public override DateTime? Parse(object value)
    {
        if (value == null || value is DBNull)
            return null;

        if (value is MySqlDateTime mysqlDateTime)
            return mysqlDateTime.IsValidDateTime ? mysqlDateTime.GetDateTime() : null;

        return (DateTime?)value;
    }
}

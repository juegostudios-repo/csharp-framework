using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Dapper;
using MySqlConnector;

namespace JuegoFramework.Helpers
{
    public static class TransactionContextManager
    {
        private static readonly AsyncLocal<(MySqlConnection Connection, MySqlTransaction Transaction)?> _current = new AsyncLocal<(MySqlConnection Connection, MySqlTransaction Transaction)?>();

        public static (MySqlConnection Connection, MySqlTransaction Transaction)? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }
    }

    class JsonStringTypeHandler<T> : SqlMapper.TypeHandler<T>
    {
        public override T? Parse(object value) => JsonSerializer.Deserialize<T>((string)value);

        public override void SetValue(IDbDataParameter parameter, T? value) => parameter.Value = JsonSerializer.Serialize(value);
    }

    public static class Operation
    {
        public static IncrementOperation Increment(int value) => new(value);
        public static DecrementOperation Decrement(int value) => new(value);
        public static NotOperation Not(object value) => new(value);
        public static InOperation In(IEnumerable<object> values) => new(values);
        public static LessThanOperation LessThan(object value) => new(value);
        public static LessThanEqualOperation LessThanEqual(object value) => new(value);
        public static GreaterThanOperation GreaterThan(object value) => new(value);
        public static GreaterThanEqualOperation GreaterThanEqual(object value) => new(value);
    }

    public record IncrementOperation(int Value);
    public record DecrementOperation(int Value);
    public record NotOperation(object Value);
    public record InOperation(IEnumerable<object> Values);
    public record LessThanOperation(object Value);
    public record LessThanEqualOperation(object Value);
    public record GreaterThanOperation(object Value);
    public record GreaterThanEqualOperation(object Value);

    public class SQLManager
    {
        static SQLManager()
        {
            // DefaultTypeMap.MatchNamesWithUnderscores = true;
        }

        private static MySqlConnection OpenConnection()
        {
            return new MySqlConnection(Global.ConnectionString);
        }

        private static string GetTableName(Type type)
        {
            var tableAttr = type.GetCustomAttribute<TableAttribute>();
            if (tableAttr != null)
            {
                return tableAttr.Name;
            }
            else
            {
                throw new Exception("Table name not found");
            }
        }

        private static void SetTypeMap<T>()
        {
            SqlMapper.SetTypeMap(
                typeof(T),
                new CustomPropertyTypeMap(
                typeof(T),
                (type, columnName) => {
                    var property = type.GetProperties().First(prop => {
                        var result = prop.GetCustomAttributes(false)
                            .OfType<ColumnAttribute>()
                            .Any(attr => attr.Name == columnName);

                        if (!result) {
                            return prop.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase);
                        }

                        return result;
                    });

                    var columnAttr = property.GetCustomAttributes(true).OfType<ColumnAttribute>().FirstOrDefault();
                    if (columnAttr?.TypeName == "JSON")
                    {
                        var handlerType = typeof(JsonStringTypeHandler<>).MakeGenericType(property.PropertyType);
                        var handler = (SqlMapper.ITypeHandler?)Activator.CreateInstance(handlerType) ?? throw new Exception($"Failed to create instance of {handlerType.Name}");
                        SqlMapper.AddTypeHandler(property.PropertyType, handler);
                    }

                    return property;
                })
            );
        }

        private static async Task<T> LogAndTimeOperation<T>(Func<MySqlConnection, MySqlTransaction?, Task<T>> operation, string operationName, MySqlConnection connection, MySqlTransaction? transaction)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await operation.Invoke(connection, transaction);
                stopwatch.Stop();

                Log.Information($"{operationName} executed in {stopwatch.ElapsedMilliseconds} ms");

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Log.Error(ex, $"{operationName} failed after {stopwatch.ElapsedMilliseconds} ms");
                throw;
            }
        }

        private static async Task<T> ExecuteDbOperation<T>(Func<MySqlConnection, MySqlTransaction?, Task<T>> operation, string operationName)
        {
            var transactionContext = TransactionContextManager.Current;
            bool isNewConnection = transactionContext == null;
            MySqlConnection conn;
            MySqlTransaction? trans = null;

            if (isNewConnection)
            {
                conn = OpenConnection();
                var stopwatch = Stopwatch.StartNew();
                await conn.OpenAsync();
                stopwatch.Stop();
                Log.Information($"Opened connection in {stopwatch.ElapsedMilliseconds} ms");
            }
            else
            {
                if (transactionContext == null)
                {
                    throw new Exception("TransactionContext is null but it should not be null because this block will only run inside a transaction.");
                }
                conn = transactionContext.Value.Connection;
                trans = transactionContext.Value.Transaction;
            }

            try
            {
                return await LogAndTimeOperation(operation, operationName, conn, trans);
            }
            finally
            {
                if (isNewConnection)
                {
                    await conn.CloseAsync();
                    conn.Dispose();
                }
            }
        }

        public static Dictionary<string, object?> ConvertToDictionary(object anonymousObject)
        {
            if (anonymousObject == null)
            {
                throw new ArgumentNullException(nameof(anonymousObject));
            }

            var dictionary = anonymousObject.GetType().GetProperties().ToDictionary(
                prop => prop.Name,
                prop => prop.GetValue(anonymousObject, null)
            );

            return dictionary;
        }

        public static async Task<T?> FindOne<T>(Dictionary<string, object?> whereClause) where T : class
        {
            var operationName = $"FindOne<{typeof(T).Name}>";

            return await ExecuteDbOperation(async (conn, trans) =>
            {
                SetTypeMap<T>();

                var where = string.Join(" AND ", whereClause.Select(x => {
                    if (x.Value is not null and NotOperation)
                    {
                        return $"{x.Key} != @{x.Key}";
                    }
                    if (x.Value is not null and InOperation)
                    {
                        return $"{x.Key} IN @{x.Key}";
                    }
                    if (x.Value is not null and LessThanOperation)
                    {
                        return $"{x.Key} < @{x.Key}";
                    }
                    if (x.Value is not null and LessThanEqualOperation)
                    {
                        return $"{x.Key} <= @{x.Key}";
                    }
                    if (x.Value is not null and GreaterThanOperation)
                    {
                        return $"{x.Key} > @{x.Key}";
                    }
                    if (x.Value is not null and GreaterThanEqualOperation)
                    {
                        return $"{x.Key} >= @{x.Key}";
                    }
                    return $"{x.Key} = @{x.Key}";
                }));

                var parameters = new DynamicParameters();

                foreach (var item in whereClause)
                {
                    if (item.Value is not null and NotOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as NotOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and InOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as InOperation)?.Values);
                        continue;
                    }
                    if (item.Value is not null and LessThanOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as LessThanOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and LessThanEqualOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as LessThanEqualOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and GreaterThanOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as GreaterThanOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and GreaterThanEqualOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as GreaterThanEqualOperation)?.Value);
                        continue;
                    }
                    parameters.Add($"@{item.Key}", item.Value);
                }

                var sql = $"SELECT * FROM {GetTableName(typeof(T))} WHERE {where}";
                var result = await conn.QueryFirstOrDefaultAsync<T>(sql, parameters, transaction: trans);
                return result;
            }, operationName);
        }

        public static async Task<T?> FindOne<T>(object whereClause) where T : class
        {
            return await FindOne<T>(ConvertToDictionary(whereClause));
        }

        public static async Task<List<T>> FindAll<T>(Dictionary<string, object?> whereClause) where T : class
        {
            var operationName = $"FindAll<{typeof(T).Name}>";

            return await ExecuteDbOperation(async (conn, trans) =>
            {
                SetTypeMap<T>();

                var where = string.Join(" AND ", whereClause.Select(x => {
                    if (x.Value is not null and NotOperation)
                    {
                        return $"{x.Key} != @{x.Key}";
                    }
                    if (x.Value is not null and InOperation)
                    {
                        return $"{x.Key} IN @{x.Key}";
                    }
                    if (x.Value is not null and LessThanOperation)
                    {
                        return $"{x.Key} < @{x.Key}";
                    }
                    if (x.Value is not null and LessThanEqualOperation)
                    {
                        return $"{x.Key} <= @{x.Key}";
                    }
                    if (x.Value is not null and GreaterThanOperation)
                    {
                        return $"{x.Key} > @{x.Key}";
                    }
                    if (x.Value is not null and GreaterThanEqualOperation)
                    {
                        return $"{x.Key} >= @{x.Key}";
                    }
                    return $"{x.Key} = @{x.Key}";
                }));

                var parameters = new DynamicParameters();

                foreach (var item in whereClause)
                {
                    if (item.Value is not null and NotOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as NotOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and InOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as InOperation)?.Values);
                        continue;
                    }
                    if (item.Value is not null and LessThanOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as LessThanOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and LessThanEqualOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as LessThanEqualOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and GreaterThanOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as GreaterThanOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and GreaterThanEqualOperation)
                    {
                        parameters.Add($"@{item.Key}", (item.Value as GreaterThanEqualOperation)?.Value);
                        continue;
                    }
                    parameters.Add($"@{item.Key}", item.Value);
                }

                if (where == "")
                {
                    where = "1 = 1";
                }

                var sql = $"SELECT * FROM {GetTableName(typeof(T))} WHERE {where}";
                var result = await conn.QueryAsync<T>(sql, parameters, transaction: trans);
                return result.ToList();
            }, operationName);
        }

        public static async Task<List<T>> FindAll<T>(object whereClause) where T : class
        {
            return await FindAll<T>(ConvertToDictionary(whereClause));
        }

        public static async Task<int> Update<T>(Dictionary<string, object?> whereClause, Dictionary<string, object?> updateData) where T : class
        {
            var operationName = $"Update<{typeof(T).Name}>";

            return await ExecuteDbOperation(async (conn, trans) =>
            {
                SetTypeMap<T>();

                var where = string.Join(" AND ", whereClause.Select(x => {
                    if (x.Value is not null and NotOperation)
                    {
                        return $"{x.Key} != @Where_{x.Key}";
                    }
                    if (x.Value is not null and InOperation)
                    {
                        return $"{x.Key} IN @Where_{x.Key}";
                    }
                    return $"{x.Key} = @Where_{x.Key}";
                }));

                var update = string.Join(", ", updateData.Select(x => {
                    if (x.Value is not null and IncrementOperation)
                    {
                        return $"{x.Key} = {x.Key} + @Update_{x.Key}";
                    }
                    if (x.Value is not null and DecrementOperation)
                    {
                        return $"{x.Key} = {x.Key} - @Update_{x.Key}";
                    }
                    return $"{x.Key} = @Update_{x.Key}";
                }));

                var sql = $"UPDATE {GetTableName(typeof(T))} SET {update} WHERE {where}";

                var parameters = new DynamicParameters();

                foreach (var item in whereClause)
                {
                    if (item.Value is not null and NotOperation)
                    {
                        parameters.Add($"@Where_{item.Key}", (item.Value as NotOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and InOperation)
                    {
                        parameters.Add($"@Where_{item.Key}", (item.Value as InOperation)?.Values);
                        continue;
                    }
                    parameters.Add($"@Where_{item.Key}", item.Value);
                }

                foreach (var item in updateData)
                {
                    if (item.Value is not null and IncrementOperation)
                    {
                        parameters.Add($"@Update_{item.Key}", (item.Value as IncrementOperation)?.Value);
                        continue;
                    }
                    if (item.Value is not null and DecrementOperation)
                    {
                        parameters.Add($"@Update_{item.Key}", (item.Value as DecrementOperation)?.Value);
                        continue;
                    }
                    parameters.Add($"@Update_{item.Key}", item.Value);
                }

                Log.Information($"Generated SQL: {sql}");

                var result = await conn.ExecuteAsync(sql, parameters, transaction: trans);
                return result;
            }, operationName);
        }

        public static async Task<int> Update<T>(object whereClause, object updateData) where T : class
        {
            Dictionary<string, object?> whereDict;
            Dictionary<string, object?> updateDict;

            if (whereClause is Dictionary<string, object?> dictionary)
            {
                whereDict = dictionary;
            }
            else
            {
                whereDict = ConvertToDictionary(whereClause);
            }

            if (updateData is Dictionary<string, object?> dictionary1)
            {
                updateDict = dictionary1;
            }
            else
            {
                updateDict = ConvertToDictionary(updateData);
            }

            return await Update<T>(whereDict, updateDict);
        }

        public static async Task<long> Insert<T>(Dictionary<string, object?> insertData) where T : class
        {
            var operationName = $"Insert<{typeof(T).Name}>";

            return await ExecuteDbOperation(async (conn, trans) =>
            {
                SetTypeMap<T>();

                var columns = string.Join(", ", insertData.Select(x => x.Key));
                var values = string.Join(", ", insertData.Select(x => $"@{x.Key}"));
                var sql = $"INSERT INTO {GetTableName(typeof(T))} ({columns}) VALUES ({values})";
                Log.Information($"Generated SQL: {sql}");
                sql += "; SELECT LAST_INSERT_ID();";
                var result = await conn.ExecuteScalarAsync<long>(sql, insertData, transaction: trans);
                Log.Information($"Inserted {JsonSerializer.Serialize(insertData)} into {GetTableName(typeof(T))} with id {result}");
                return result;
            }, operationName);
        }

        public static async Task<long> Insert<T>(object insertData) where T : class
        {
            return await Insert<T>(ConvertToDictionary(insertData));
        }

        public static async Task<long> Insert<T>(T obj) where T : class
        {
            var insertData = new Dictionary<string, object?>();
            foreach (var prop in typeof(T).GetProperties())
            {
                var columnAttr = prop.GetCustomAttributes(true).OfType<ColumnAttribute>().FirstOrDefault();
                if (columnAttr != null)
                {
                    insertData.Add(columnAttr.Name ?? throw new Exception("columnAttr.Name should never be null"), prop.GetValue(obj));
                }
                else
                {
                    insertData.Add(prop.Name, prop.GetValue(obj));
                }
            }

            return await Insert<T>(insertData);
        }

        public static async Task<List<long>> Insert<T>(List<T> objs) where T : class
        {
            List<long> insertedIds = [];

            foreach (var obj in objs)
            {
                long insertedId = await Insert(obj);
                insertedIds.Add(insertedId);
            }

            return insertedIds;
        }

        public static async Task<List<T>> Query<T>(string sql, Dictionary<string, object?> parameters)
        {
            var operationName = $"Query<{typeof(T).Name}>";

            return await ExecuteDbOperation(async (conn, trans) =>
            {
                SetTypeMap<T>();

                var result = await conn.QueryAsync<T>(sql, parameters, transaction: trans);
                return result.ToList();
            }, operationName);
        }

        public static async Task<List<T>> Query<T>(string sql, object parameters)
        {
            return await Query<T>(sql, ConvertToDictionary(parameters));
        }

        public static async Task<List<T>> Query<T>(string sql)
        {
            return await Query<T>(sql, []);
        }

        public static async Task<T?> QueryOne<T>(string sql, Dictionary<string, object?> parameters)
        {
            var operationName = $"QueryOne<{typeof(T).Name}>";

            return await ExecuteDbOperation(async (conn, trans) =>
            {
                SetTypeMap<T>();

                var result = await conn.QueryFirstOrDefaultAsync<T>(sql, parameters, transaction: trans);
                return result;
            }, operationName);
        }

        public static async Task<T?> QueryOne<T>(string sql, object parameters)
        {
            return await QueryOne<T>(sql, ConvertToDictionary(parameters));
        }

        public static async Task<T?> QueryOne<T>(string sql)
        {
            return await QueryOne<T>(sql, []);
        }

        public static async Task<int> Execute(string sql, Dictionary<string, object?> parameters)
        {
            var operationName = "Execute";

            return await ExecuteDbOperation(async (conn, trans) =>
            {
                var result = await conn.ExecuteAsync(sql, parameters, transaction: trans);
                return result;
            }, operationName);
        }

        public static async Task<int> Execute(string sql, object parameters)
        {
            return await Execute(sql, ConvertToDictionary(parameters));
        }

        public static async Task<int> Execute(string sql)
        {
            return await Execute(sql, []);
        }

        public static async Task Transaction(Func<Task> action)
        {
            using var connection = OpenConnection();

            var stopwatch = Stopwatch.StartNew();
            await connection.OpenAsync();
            stopwatch.Stop();
            Log.Information($"Opened connection in {stopwatch.ElapsedMilliseconds} ms");

            using var transaction = await connection.BeginTransactionAsync();

            TransactionContextManager.Current = (connection, transaction);

            try
            {
                await action();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw new Exception("Transaction failed");
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
    }
}

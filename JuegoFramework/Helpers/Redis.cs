using System.Text.Json;
using StackExchange.Redis;

namespace JuegoFramework.Helpers;

public class Redis()
{
    private static readonly string redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "";
    private static readonly string redisPrefixKey = Environment.GetEnvironmentVariable("REDIS_PREFIX_KEY") ?? "";
    private static ConnectionMultiplexer? _redis;

    public static ConnectionMultiplexer Redis2
    {
        get
        {
            _redis ??= ConnectionMultiplexer.Connect(redisConnectionString);

            return _redis;
        }
    }

    public static IDatabase Database => Redis2.GetDatabase();

    public static async Task Set(RedisKey key, object value, string prefix)
    {
        IDatabase _db = Database;
        prefix = redisPrefixKey + prefix;
        string json = JsonSerializer.Serialize(value);
        await _db.StringSetAsync(key.Append(prefix + "-"), json);
    }

    public static async Task<T?> Get<T>(RedisKey key, string prefix)
    {
        IDatabase _db = Database;
        prefix = redisPrefixKey + prefix;
        string? json = await _db.StringGetAsync(key.Append(prefix + "-"));
        if (json == null) return default;
        return JsonSerializer.Deserialize<T>(json);
    }

    public static async Task<bool> AcquireLockAsync(string lockName, string lockValue, TimeSpan expiryTime)
    {
        IDatabase db = Database;
        var lockResult = await db.LockTakeAsync(lockName, lockValue, expiryTime);
        return lockResult;
    }

    public static async Task ReleaseLockAsync(string lockName, string lockValue)
    {
        IDatabase db = Database;
        await db.LockReleaseAsync(lockName, lockValue);
    }
}

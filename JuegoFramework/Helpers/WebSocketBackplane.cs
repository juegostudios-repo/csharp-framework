using System.Text.Json;
using StackExchange.Redis;

namespace JuegoFramework.Helpers
{
    // Cross-instance WebSocket delivery over Redis pub/sub for SERVER_CLUSTER mode.
    // Each instance owns a channel "{prefix}:ws:inst:{instanceId}". A send to a
    // connection owned by another instance is published to that instance's channel;
    // the owning instance delivers it to its local socket.
    // See docs/superpowers/specs/2026-06-06-autoscaling-websocket-design.md.
    public static class WebSocketBackplane
    {
        // Embedded in every cluster connection id so any instance can route to the
        // owner without a separate registry, and used as this instance's Redis channel.
        // A fresh GUID per process, assigned at startup. Intentionally NOT configurable:
        // a value shared across instances (e.g. one set in an ECS task definition, which
        // applies to every task) would collide channels and misroute messages. Each
        // instance must self-assign a unique id.
        public static readonly string InstanceId = Guid.NewGuid().ToString("N");

        private static readonly string Prefix =
            Environment.GetEnvironmentVariable("REDIS_PREFIX_KEY") ?? "";

        public const string PongMessage = "{\"type\":\"pong\"}";

        public sealed class Envelope
        {
            // Exactly one of these is set: ConnectionId for a single send,
            // ConnectionIds for a batched fan-out to one instance.
            public string? ConnectionId { get; set; }
            public List<string>? ConnectionIds { get; set; }
            public required string Message { get; set; }
        }

        public static string ChannelFor(string instanceId) => $"{Prefix}:ws:inst:{instanceId}";

        public static string NewConnectionId() => $"{InstanceId}:{Guid.NewGuid():N}";

        // connectionId format is "{instanceId}:{guid}". Returns false when there is
        // no non-empty instance segment before the first ':'.
        public static bool TryGetInstanceId(string connectionId, out string instanceId)
        {
            instanceId = "";
            if (string.IsNullOrEmpty(connectionId)) return false;
            int i = connectionId.IndexOf(':');
            if (i <= 0) return false;
            instanceId = connectionId[..i];
            return true;
        }

        public static bool IsForThisInstance(string connectionId) =>
            TryGetInstanceId(connectionId, out var id) && id == InstanceId;

        public static Dictionary<string, List<string>> GroupByInstance(IEnumerable<string> connectionIds)
        {
            var map = new Dictionary<string, List<string>>();
            foreach (var cid in connectionIds)
            {
                if (!TryGetInstanceId(cid, out var inst)) continue;
                if (!map.TryGetValue(inst, out var list)) { list = new(); map[inst] = list; }
                list.Add(cid);
            }
            return map;
        }

        public static bool IsPingMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            try
            {
                using var doc = JsonDocument.Parse(message);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("type", out var t)
                    && t.ValueKind == JsonValueKind.String
                    && t.GetString() == "ping";
            }
            catch
            {
                return false;
            }
        }

        public static async Task PublishAsync(string instanceId, Envelope envelope)
        {
            var sub = Redis.Redis2.GetSubscriber();
            var json = JsonSerializer.Serialize(envelope);
            await sub.PublishAsync(RedisChannel.Literal(ChannelFor(instanceId)), json);
        }

        private static int _started;

        // Subscribes this instance's channel once at startup. `deliver` pushes an
        // envelope's message to the locally-held socket(s). Idempotent: a second
        // call is a no-op, so a duplicated startup path cannot double-deliver.
        public static async Task StartAsync(Func<Envelope, Task> deliver)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            {
                return;
            }

            var channel = ChannelFor(InstanceId);
            var sub = Redis.Redis2.GetSubscriber();
            await sub.SubscribeAsync(RedisChannel.Literal(channel), async (_, value) =>
            {
                if (value.IsNullOrEmpty) return;
                try
                {
                    var env = JsonSerializer.Deserialize<Envelope>((string)value!);
                    if (env != null) await deliver(env);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "WebSocketBackplane: deliver failed");
                }
            });
            Log.Information("WebSocketBackplane: instance {InstanceId} subscribed to {Channel}", InstanceId, channel);
        }
    }
}

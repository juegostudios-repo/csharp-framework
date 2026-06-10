using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace JuegoFramework.Helpers
{
    // Generalized instance routing for a project fan-out that must interpose per-connection
    // processing (e.g. coalescing) BEFORE the socket write -- so it cannot ride
    // WebSocketBackplane, which delivers straight to sockets with no project seam.
    //
    // The websocket-mode awareness lives HERE, once. In non-cluster modes (SERVER / AWS /
    // AZURE) every connection is local, so every payload is handed straight to the local
    // sink. In SERVER_CLUSTER the connections are partitioned by owning instance: locals go
    // to the sink, remotes are batched per instance and forwarded over Redis, and the owning
    // instance's subscriber runs the SAME sink on arrival. Project code supplies only an
    // opaque payload and a local sink; it never inspects USE_WEBSOCKET_SYSTEM, the
    // connection-id format, or the channel.
    //
    // Channel: "{REDIS_PREFIX_KEY}:{channelBase}:{instanceId}" -- parallel to
    // WebSocketBackplane's "{prefix}:ws:inst:{id}" but project-named, so the subscriber can
    // route into the project sink instead of a socket. Pass channelBase WITHOUT the prefix
    // or the instance segment (e.g. "march:inst"); this owns both.
    //
    // T is the payload carried to the sink. It is serialized only when it crosses the wire
    // to a remote instance; the local path hands the in-memory instance to the sink with no
    // serialize/deserialize round-trip.
    public sealed class InstanceFanout<T>
    {
        private static readonly string Prefix =
            Environment.GetEnvironmentVariable("REDIS_PREFIX_KEY") ?? "";

        private readonly string _channelBase;
        private readonly Action<string, T> _sink;
        private int _started;

        // sink: invoked as (connectionId, payload) on whichever instance owns the
        // connection. Must be safe to call from the Redis subscriber thread (the cluster
        // path) as well as the producer thread (the local path).
        public InstanceFanout(string channelBase, Action<string, T> sink)
        {
            _channelBase = channelBase;
            _sink = sink;
        }

        private string ChannelFor(string instanceId) => $"{Prefix}:{_channelBase}:{instanceId}";

        private sealed class Envelope
        {
            [JsonPropertyName("connectionIds")] public List<string> ConnectionIds { get; set; } = new();
            [JsonPropertyName("payload")] public T Payload { get; set; } = default!;
        }

        // Subscribes this instance's channel so payloads forwarded by peer instances reach
        // the local sink. Meaningful only in SERVER_CLUSTER (in other modes every recipient
        // is local, so nothing is ever forwarded); a no-op otherwise. Idempotent: a second
        // call cannot double-subscribe. Call once at startup.
        public async Task StartAsync()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            {
                return;
            }

            if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") != "SERVER_CLUSTER")
            {
                return;
            }

            var channel = ChannelFor(WebSocketBackplane.InstanceId);
            var sub = Redis.Redis2.GetSubscriber();
            await sub.SubscribeAsync(RedisChannel.Literal(channel), (_, value) =>
            {
                if (value.IsNullOrEmpty)
                {
                    return;
                }

                try
                {
                    var env = JsonSerializer.Deserialize<Envelope>(value!);
                    if (env == null)
                    {
                        return;
                    }

                    foreach (var connectionId in env.ConnectionIds)
                    {
                        _sink(connectionId, env.Payload);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "InstanceFanout: failed to route inbound envelope on {Channel}", channel);
                }
            });

            Log.Information("InstanceFanout: instance {InstanceId} subscribed to {Channel}",
                WebSocketBackplane.InstanceId, channel);
        }

        // Routes one payload to a set of connections. Local connections invoke the sink
        // inline; remote connections (SERVER_CLUSTER only) are batched per owning instance
        // and forwarded, where the remote subscriber invokes the same sink. In non-cluster
        // modes every connection is local. An unroutable id (a bare id while clustered) is
        // skipped rather than misrouted.
        public async Task RouteAsync(IReadOnlyCollection<string> connectionIds, T payload)
        {
            if (connectionIds.Count == 0)
            {
                return;
            }

            if (Environment.GetEnvironmentVariable("USE_WEBSOCKET_SYSTEM") != "SERVER_CLUSTER")
            {
                foreach (var cid in connectionIds)
                {
                    _sink(cid, payload);
                }
                return;
            }

            Dictionary<string, List<string>>? remotes = null;
            foreach (var cid in connectionIds)
            {
                if (WebSocketBackplane.IsForThisInstance(cid))
                {
                    _sink(cid, payload);
                    continue;
                }
                if (!WebSocketBackplane.TryGetInstanceId(cid, out var inst))
                {
                    continue; // unroutable id -- no owning instance to forward to
                }
                remotes ??= new();
                if (!remotes.TryGetValue(inst, out var list)) { list = new(); remotes[inst] = list; }
                list.Add(cid);
            }

            if (remotes == null)
            {
                return;
            }

            var sub = Redis.Redis2.GetSubscriber();
            var publishes = new List<Task>(remotes.Count);
            foreach (var (inst, cids) in remotes)
            {
                var json = JsonSerializer.Serialize(new Envelope { ConnectionIds = cids, Payload = payload });
                publishes.Add(sub.PublishAsync(RedisChannel.Literal(ChannelFor(inst)), json));
            }
            await Task.WhenAll(publishes);
        }
    }
}

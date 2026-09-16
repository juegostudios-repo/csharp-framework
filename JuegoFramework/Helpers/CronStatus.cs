using System.Globalization;
using StackExchange.Redis;

namespace JuegoFramework.Helpers
{
    /// <summary>
    /// What one fan out tick did: how many items <c>Enumerate</c> returned, how many claims this
    /// container won, how many it lost to other containers, how many of its own failed, and how
    /// many claims it released after a success.
    /// </summary>
    /// <param name="Enumerated">Items returned by Enumerate.</param>
    /// <param name="Claimed">Claims this container won.</param>
    /// <param name="Skipped">Items another container held or that were backing off.</param>
    /// <param name="Failed">Items whose Process threw.</param>
    /// <param name="Released">Claims released after a successful Process.</param>
    public sealed record FanOutTick(int Enumerated, int Claimed, int Skipped, int Failed, int Released)
    {
        /// <summary>
        /// A tick that claimed nothing and failed nothing changed no state, so it only refreshes the
        /// status hash and is not appended to the run history.
        /// </summary>
        internal bool DidWork => Claimed > 0 || Failed > 0;
    }

    /// <summary>
    /// The shape of one job and its latest run, read back from the status hash. Everything after
    /// <see cref="Concurrency"/> is null until the job has run once.
    /// </summary>
    /// <param name="Name">The job's class name.</param>
    /// <param name="Kind">One of <see cref="CronStatus.KIND_INTERVAL"/>, <see cref="CronStatus.KIND_SCHEDULED"/> or <see cref="CronStatus.KIND_FAN_OUT"/>.</param>
    /// <param name="Interval">The interval of an interval or fan out job.</param>
    /// <param name="Expression">The cron expression of a scheduled job.</param>
    /// <param name="Concurrency">The per container concurrency of a fan out job.</param>
    /// <param name="NextRunAt">When the loop last decided to run next. For a fan out job it is the latest container's answer.</param>
    /// <param name="LastStartedAt">When the latest run started.</param>
    /// <param name="LastFinishedAt">When the latest finished run finished.</param>
    /// <param name="LastOutcome">One of the <c>CronStatus.OUTCOME_*</c> values.</param>
    /// <param name="LastError">The exception type and message of the latest failed run, null when it succeeded.</param>
    /// <param name="LastInstance">The container that ran last, as <c>{machine name}:{8 hex}</c>.</param>
    /// <param name="LastTick">The latest fan out tick's counts, null for other kinds.</param>
    /// <param name="StoppedAt">When the job's loop ended, null while it is scheduled. A worker restart clears it.</param>
    /// <param name="StoppedReason">Why the loop ended.</param>
    public sealed record CronJobStatus(
        string Name,
        string Kind,
        TimeSpan? Interval,
        string? Expression,
        int? Concurrency,
        DateTime? NextRunAt,
        DateTime? LastStartedAt,
        DateTime? LastFinishedAt,
        string? LastOutcome,
        string? LastError,
        string? LastInstance,
        FanOutTick? LastTick,
        DateTime? StoppedAt,
        string? StoppedReason)
    {
        /// <summary>
        /// True when the latest start has no finish after it. Exact for an interval or scheduled
        /// job; for a fan out job, whose containers tick independently, it reflects the container
        /// that wrote last. A container that died mid run leaves this true until the next run.
        /// </summary>
        public bool Running => LastStartedAt is not null && (LastFinishedAt is null || LastFinishedAt < LastStartedAt);
    }

    /// <summary>
    /// One past run, read back from the run history.
    /// </summary>
    /// <param name="Id">The stream entry id, passed as <c>before</c> to page further back.</param>
    /// <param name="Instance">The container that ran it.</param>
    /// <param name="StartedAt">When the run started.</param>
    /// <param name="FinishedAt">When the run finished.</param>
    /// <param name="Outcome">One of the <c>CronStatus.OUTCOME_*</c> values.</param>
    /// <param name="Error">The exception type and message when it failed.</param>
    /// <param name="Tick">The fan out counts, null for other kinds.</param>
    public sealed record CronRun(
        string Id,
        string Instance,
        DateTime StartedAt,
        DateTime FinishedAt,
        string Outcome,
        string? Error,
        FanOutTick? Tick)
    {
        /// <summary>
        /// How long the run took.
        /// </summary>
        public TimeSpan Duration => FinishedAt - StartedAt;
    }

    /// <summary>
    /// Records every job's latest run in a Redis hash and its past runs in a capped Redis stream,
    /// and reads them back, so any process on the same Redis (a web instance serving an admin page,
    /// say) can see what the workers are doing. Recording is a no-op without
    /// <c>REDIS_CONNECTION_STRING</c>, and a Redis failure while recording is logged and does not
    /// affect the run. The keys are <c>{prefix}:cron:jobs</c>, <c>{prefix}:cron:{Job}:status</c>
    /// and <c>{prefix}:cron:{Job}:runs</c>.
    /// </summary>
    public static class CronStatus
    {
        /// <summary>A fixed interval job.</summary>
        public const string KIND_INTERVAL = "interval";

        /// <summary>A cron expression job.</summary>
        public const string KIND_SCHEDULED = "scheduled";

        /// <summary>A job spread across the fleet item by item.</summary>
        public const string KIND_FAN_OUT = "fan_out";

        /// <summary>The run finished without throwing.</summary>
        public const string OUTCOME_OK = "ok";

        /// <summary>The run threw.</summary>
        public const string OUTCOME_FAILED = "failed";

        /// <summary>The run was cut short by a shutdown.</summary>
        public const string OUTCOME_CANCELLED = "cancelled";

        /// <summary>
        /// How many past runs a job keeps. The stream is trimmed approximately, so a few more may
        /// linger.
        /// </summary>
        public const int RUN_HISTORY_LENGTH = 10_000;

        /// <summary>
        /// The most runs one <see cref="RunsAsync"/> call returns.
        /// </summary>
        public const int MAX_RUNS_PAGE = 1_000;

        private const int MAX_ERROR_LENGTH = 2_000;

        private const string F_KIND = "kind";
        private const string F_INTERVAL_MS = "interval_ms";
        private const string F_EXPRESSION = "expression";
        private const string F_CONCURRENCY = "concurrency";
        private const string F_NEXT_RUN_AT = "next_run_at";
        private const string F_LAST_STARTED_AT = "last_started_at";
        private const string F_LAST_FINISHED_AT = "last_finished_at";
        private const string F_LAST_OUTCOME = "last_outcome";
        private const string F_LAST_ERROR = "last_error";
        private const string F_LAST_INSTANCE = "last_instance";
        private const string F_STOPPED_AT = "stopped_at";
        private const string F_STOPPED_REASON = "stopped_reason";
        private const string F_ENUMERATED = "enumerated";
        private const string F_CLAIMED = "claimed";
        private const string F_SKIPPED = "skipped";
        private const string F_FAILED = "failed";
        private const string F_RELEASED = "released";

        private const string R_INSTANCE = "instance";
        private const string R_STARTED_AT = "started_at";
        private const string R_FINISHED_AT = "finished_at";
        private const string R_OUTCOME = "outcome";
        private const string R_ERROR = "error";

        /// <summary>
        /// Identifies this process in the status hash and the run history: the machine name, which
        /// is the container id under Docker, plus eight hex characters so two processes on one host
        /// still differ.
        /// </summary>
        internal static readonly string INSTANCE_ID = $"{Environment.MachineName}:{Guid.NewGuid().ToString("N")[..8]}";

        /// <summary>
        /// Every job any worker has started, with its latest run, ordered by name. Empty without
        /// Redis configured.
        /// </summary>
        /// <returns>One status per job.</returns>
        public static async Task<IReadOnlyList<CronJobStatus>> ListAsync()
        {
            if (!CronRedisKeys.IsConfigured)
            {
                return [];
            }

            var database = Redis.Database;
            var names = await database.SetMembersAsync(CronRedisKeys.Jobs);
            var reads = names
                .Select(name => name.ToString())
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(async name => ToStatus(name, await database.HashGetAllAsync(CronRedisKeys.Status(name))))
                .ToList();

            return [.. (await Task.WhenAll(reads)).OfType<CronJobStatus>()];
        }

        /// <summary>
        /// One job's latest run, or null when no worker has started a job by that name.
        /// </summary>
        /// <param name="jobName">The job's class name.</param>
        /// <returns>The job's status, or null.</returns>
        public static async Task<CronJobStatus?> GetAsync(string jobName)
        {
            ArgumentException.ThrowIfNullOrEmpty(jobName);

            if (!CronRedisKeys.IsConfigured)
            {
                return null;
            }

            // A missing hash reads back empty, which ToStatus reports as null.
            return ToStatus(jobName, await Redis.Database.HashGetAllAsync(CronRedisKeys.Status(jobName)));
        }

        /// <summary>
        /// A job's past runs, newest first. Pass the last returned <see cref="CronRun.Id"/> as
        /// <paramref name="before"/> to page further back.
        /// </summary>
        /// <param name="jobName">The job's class name.</param>
        /// <param name="count">How many runs to return, 1 to <see cref="MAX_RUNS_PAGE"/>.</param>
        /// <param name="before">Return only runs older than this entry id, or null to start from the newest.</param>
        /// <returns>The runs, newest first. Empty without Redis configured or for an unknown job.</returns>
        public static async Task<IReadOnlyList<CronRun>> RunsAsync(string jobName, int count = 100, string? before = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(jobName);
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MAX_RUNS_PAGE);

            if (!CronRedisKeys.IsConfigured)
            {
                return [];
            }

            // XREVRANGE is inclusive at both ends, so ask for one more and drop the cursor entry.
            // A missing stream answers with no entries, so an unknown job needs no separate check.
            var entries = await Redis.Database.StreamRangeAsync(
                CronRedisKeys.Runs(jobName),
                minId: "-",
                maxId: before ?? "+",
                count: before is null ? count : count + 1,
                messageOrder: Order.Descending);

            return [.. entries
                .Where(entry => before is null || entry.Id != before)
                .Take(count)
                .Select(ToRun)];
        }

        internal static Task RegisterAsync(string jobName, string kind, TimeSpan? interval, string? expression, int? concurrency)
        {
            return RecordAsync(jobName, async database =>
            {
                var fields = new List<HashEntry> { new(F_KIND, kind) };

                if (interval is not null)
                {
                    fields.Add(new HashEntry(F_INTERVAL_MS, (long)interval.Value.TotalMilliseconds));
                }

                if (expression is not null)
                {
                    fields.Add(new HashEntry(F_EXPRESSION, expression));
                }

                if (concurrency is not null)
                {
                    fields.Add(new HashEntry(F_CONCURRENCY, concurrency.Value));
                }

                var statusKey = CronRedisKeys.Status(jobName);

                await Task.WhenAll(
                    database.SetAddAsync(CronRedisKeys.Jobs, jobName),
                    database.HashSetAsync(statusKey, [.. fields]),
                    database.HashDeleteAsync(statusKey, [F_STOPPED_AT, F_STOPPED_REASON]));
            });
        }

        internal static Task RecordNextRunAsync(string jobName, DateTime nextRunUtc)
        {
            return RecordAsync(jobName, database => database.HashSetAsync(CronRedisKeys.Status(jobName), F_NEXT_RUN_AT, Format(nextRunUtc)));
        }

        internal static Task RecordStartAsync(string jobName, DateTime startedUtc)
        {
            return RecordAsync(jobName, database => database.HashSetAsync(
                CronRedisKeys.Status(jobName),
                [
                    new HashEntry(F_LAST_STARTED_AT, Format(startedUtc)),
                    new HashEntry(F_LAST_INSTANCE, INSTANCE_ID),
                ]));
        }

        /// <summary>
        /// Writes the run into the status hash, and appends it to the run history unless it was a
        /// fan out tick that neither claimed nor failed anything: those happen up to four times a
        /// second per container while marches are due and would only bury the ticks that did work.
        /// </summary>
        internal static Task RecordFinishAsync(string jobName, DateTime startedUtc, DateTime finishedUtc, string outcome, Exception? error, FanOutTick? tick)
        {
            return RecordAsync(jobName, database =>
            {
                var errorText = error is null ? "" : Describe(error);

                var status = new List<HashEntry>
                {
                    new(F_LAST_STARTED_AT, Format(startedUtc)),
                    new(F_LAST_FINISHED_AT, Format(finishedUtc)),
                    new(F_LAST_OUTCOME, outcome),
                    new(F_LAST_ERROR, errorText),
                    new(F_LAST_INSTANCE, INSTANCE_ID),
                };

                var run = new List<NameValueEntry>
                {
                    new(R_INSTANCE, INSTANCE_ID),
                    new(R_STARTED_AT, Format(startedUtc)),
                    new(R_FINISHED_AT, Format(finishedUtc)),
                    new(R_OUTCOME, outcome),
                    new(R_ERROR, errorText),
                };

                if (tick is not null)
                {
                    HashEntry[] counts =
                    [
                        new(F_ENUMERATED, tick.Enumerated),
                        new(F_CLAIMED, tick.Claimed),
                        new(F_SKIPPED, tick.Skipped),
                        new(F_FAILED, tick.Failed),
                        new(F_RELEASED, tick.Released),
                    ];

                    status.AddRange(counts);
                    run.AddRange(counts.Select(count => new NameValueEntry(count.Name, count.Value)));
                }

                var writeStatus = database.HashSetAsync(CronRedisKeys.Status(jobName), [.. status]);

                var worthKeeping = tick is null || tick.DidWork || outcome != OUTCOME_OK;

                if (!worthKeeping)
                {
                    return writeStatus;
                }

                var writeRun = database.StreamAddAsync(
                    CronRedisKeys.Runs(jobName),
                    [.. run],
                    maxLength: RUN_HISTORY_LENGTH,
                    useApproximateMaxLength: true);

                return Task.WhenAll(writeStatus, writeRun);
            });
        }

        internal static Task RecordStoppedAsync(string jobName, string reason)
        {
            return RecordAsync(jobName, database => database.HashSetAsync(
                CronRedisKeys.Status(jobName),
                [
                    new HashEntry(F_STOPPED_AT, Format(DateTime.UtcNow)),
                    new HashEntry(F_STOPPED_REASON, reason),
                ]));
        }

        private static async Task RecordAsync(string jobName, Func<IDatabase, Task> write)
        {
            if (!CronRedisKeys.IsConfigured)
            {
                return;
            }

            try
            {
                await write(Redis.Database);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Cron {Name} could not record its status in Redis", jobName);
            }
        }

        private static CronJobStatus? ToStatus(string jobName, HashEntry[] entries)
        {
            if (entries.Length == 0)
            {
                return null;
            }

            var fields = entries.ToDictionary(entry => entry.Name.ToString(), entry => entry.Value, StringComparer.Ordinal);

            var intervalMs = Int(fields, F_INTERVAL_MS);

            return new CronJobStatus(
                jobName,
                Text(fields, F_KIND) ?? KIND_INTERVAL,
                intervalMs is null ? null : TimeSpan.FromMilliseconds(intervalMs.Value),
                Text(fields, F_EXPRESSION),
                Int(fields, F_CONCURRENCY),
                Time(fields, F_NEXT_RUN_AT),
                Time(fields, F_LAST_STARTED_AT),
                Time(fields, F_LAST_FINISHED_AT),
                Text(fields, F_LAST_OUTCOME),
                Text(fields, F_LAST_ERROR),
                Text(fields, F_LAST_INSTANCE),
                Tick(fields),
                Time(fields, F_STOPPED_AT),
                Text(fields, F_STOPPED_REASON));
        }

        private static CronRun ToRun(StreamEntry entry)
        {
            var fields = entry.Values.ToDictionary(value => value.Name.ToString(), value => value.Value, StringComparer.Ordinal);

            return new CronRun(
                entry.Id.ToString(),
                Text(fields, R_INSTANCE) ?? "",
                Time(fields, R_STARTED_AT) ?? DateTime.MinValue,
                Time(fields, R_FINISHED_AT) ?? DateTime.MinValue,
                Text(fields, R_OUTCOME) ?? OUTCOME_OK,
                Text(fields, R_ERROR),
                Tick(fields));
        }

        private static FanOutTick? Tick(Dictionary<string, RedisValue> fields)
        {
            var enumerated = Int(fields, F_ENUMERATED);

            if (enumerated is null)
            {
                return null;
            }

            return new FanOutTick(
                enumerated.Value,
                Int(fields, F_CLAIMED) ?? 0,
                Int(fields, F_SKIPPED) ?? 0,
                Int(fields, F_FAILED) ?? 0,
                Int(fields, F_RELEASED) ?? 0);
        }

        private static string? Text(Dictionary<string, RedisValue> fields, string name)
        {
            return fields.TryGetValue(name, out var value) && !value.IsNullOrEmpty ? value.ToString() : null;
        }

        private static int? Int(Dictionary<string, RedisValue> fields, string name)
        {
            return fields.TryGetValue(name, out var value) && value.TryParse(out int parsed) ? parsed : null;
        }

        private static DateTime? Time(Dictionary<string, RedisValue> fields, string name)
        {
            var text = Text(fields, name);

            if (text is null)
            {
                return null;
            }

            return DateTime.TryParseExact(text, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
        }

        private static string Format(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);
        }

        private static string Describe(Exception error)
        {
            var text = $"{error.GetType().Name}: {error.Message}";

            return text.Length <= MAX_ERROR_LENGTH ? text : text[..MAX_ERROR_LENGTH];
        }
    }
}

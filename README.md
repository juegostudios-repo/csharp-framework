# Juego Framework

## SQLManager Usage Examples

### FindOne

```csharp
var user = await SQLManager.FindOne<User>(new {
    Id = 1
});
```

### FindAll

```csharp
var users = await SQLManager.FindAll<User>(new {
    IsActive = true
});
```

### Update

```csharp
var rowsAffected = await SQLManager.Update<User>(
    new {
        Id = 1
    },
    new {
        Name = "New Name"
    }
);
```

### Insert

```csharp
var newUserId = await SQLManager.Insert<User>(new {
    Name = "John Doe",
    Email = "john.doe@example.com"
});
```

### Query

```csharp
var users = await SQLManager.Query<User>("SELECT * FROM Users WHERE IsActive = @IsActive", new {
    IsActive = true
});
```

### Execute

```csharp
var rowsAffected = await SQLManager.Execute("UPDATE Users SET IsActive = @IsActive WHERE Id = @Id", new {
    IsActive = false,
    Id = 1
});
```

### Transaction

```csharp
await SQLManager.Transaction(async () =>
{
    await SQLManager.Update<User>(new {
        Id = 1
    }, new {
        Name = "Updated Name"
    });
    await SQLManager.Insert<User>(new {
        Name = "New User",
        Email = "new.user@example.com"
    });
});
```

### Operation Usages

```csharp
var users = await SQLManager.FindAll<User>(new {
    Age = Operation.GreaterThan(18),
    IsActive = Operation.Not(false)
});

var rowsAffected = await SQLManager.Update<User>(
    new {
        Id = 1
    },
    new {
        Age = Operation.Increment(1)
    }
);

var users = await SQLManager.FindAll<User>(new {
    Age = Operation.LessThanEqual(30),
    Country = Operation.In(["USA", "Canada"])
});
```

## Cron jobs

A container started with `MODE=CRON` runs every concrete cron job found in the entry assembly.
Discovery walks each type's base classes (not just direct subclasses), so a shared intermediate
base can sit between a job and its cron base class. Abstract classes and open generics are skipped.
A job is identified everywhere (Redis keys, wakes, logs) by its bare class name, so two jobs with
the same class name in different namespaces are refused at startup.
There are three kinds of job:

- **`Cron`**, a fixed interval job. Override `Interval` and `Run(CancellationToken)`.
- **`ScheduledCron`**, a Cronos cron expression with seconds. Override `Expression` and
  `Run(CancellationToken)`.
- **`FanOutCron<TItem>`**, a fixed interval job spread across the fleet item by item. Override
  `Enumerate(CancellationToken)` and `Process(TItem, CancellationToken)` instead of `Run`.

The token is cancelled when the process is shutting down. A long run or item passes it to the work
it awaits, or polls it between steps, so the worker can drain; a short one ignores it. A fan out
item that throws `OperationCanceledException` once the token is cancelled has its claim released
rather than kept, so the next tick anywhere takes it straight away.

### Construction

Jobs are constructed once, at worker start, through the application's service provider
(`ActivatorUtilities`), so a job takes its dependencies in its constructor like any other service.
The job lives for the whole process, so a scoped dependency is not injected directly (the root
provider refuses it in Development): inject `IServiceScopeFactory` and open a scope per run or per
item. A job whose dependencies the provider cannot supply is named in the startup error. Without a
provider (a project that does not call `Application.InitApp`, or a test), the parameterless
constructor is used.

### Timing

A `Cron` or `ScheduledCron` runs once per tick across the whole fleet. How that happens depends on
whether Redis is configured:

- **With `REDIS_CONNECTION_STRING`**, interval ticks align to the clock
  (`floor(nowUnix / Interval) * Interval + Interval`) and a cron expression's occurrences are already
  aligned, so every container computes the same slot. The slot is claimed with `SET NX`; the winner
  runs and the others skip. The winner also holds a running lock for the duration of `Run`,
  refreshed every 20 seconds, so a slot whose previous run is still in flight anywhere in the fleet
  is skipped rather than doubled up. Nothing to configure per job.
- **Without Redis**, the job runs on its own timer, which is the single container case. A `Cron`'s
  interval is then measured from the end of the previous run, so the first run happens one interval
  after start and a slow run pushes the next one back rather than overlapping it.

`FanOutCron<TItem>` claims each item returned by `Enumerate` with `SET NX PX ItemLease` in one
pipelined batch, then processes the ones it won with up to `Concurrency` items in parallel. The
contract is at least once, not exactly once: an item whose processing outlives `ItemLease` may be
taken by another container, and since a finished item's claim is released straight away, a container
enumerating at about the same moment can claim an item another has just finished and released, and
process it again. `Process` must therefore be safe to repeat. A job whose `Enumerate` cannot tell
"done" from "still needs work" on its own records the last pass on the item, a timestamp column for
instance, and enumerates on that. A per item failure is logged and keeps that item's claim, so a
broken item backs off for `ItemLease` instead of retrying in a tight loop. An item whose `Process`
outlives `ItemLease` logs a warning, since another container may have taken it meanwhile.
`ItemKey` defaults to the item's `ToString`, which is right for a number, a string or a Guid; for a
class it is the type name, so override it, or every item shares one claim (duplicate keys in a tick
are logged as an error). With `Concurrency` above one, `Process` runs on several threads at once
and must not share mutable state between items. Above roughly 100k items per tick, shard the job
(for example by a key range) instead of enumerating everything in one job.

For a fan out job, `Interval` is the cap on the sleep between ticks and the default `ItemLease`,
not a bound on a tick's duration: a tick over many items may outlast it, so the "run took longer
than its interval" warning single runner jobs get does not apply. The lease warning covers a slow
item.

### Waking a fan out job early

A fan out job ticks on every container (its item claims are what split the work), so it is the one
kind that can react to a wake. Override `NextDueIn` on a `FanOutCron<TItem>` to return how long
until the job's next real work, or `null` for "use the interval". It is a delay rather than a timestamp, so a job can compute it against the
database clock without app/database clock skew mattering. The runner sleeps for
`clamp(value ?? Interval, 250ms, Interval)`, so `Interval` stays the cap and a "nothing due yet"
answer can never spin the loop.

Overriding `NextDueIn` also subscribes the job to the wake channel. Call
`CronWake.PublishAsync<MyJob>()` from anywhere, typically a web instance right after writing
the row the job cares about, to cut that job's current sleep short instead of waiting out the rest
of the interval. A wake is a hint, never a guarantee: it can be missed if no worker is listening, so
the job must still make progress on its own schedule regardless. A wake for a job that is not a
fan out job is ignored with a Debug log line. A wake a job publishes for itself from inside its own
`Process` is dropped without reaching Redis: the loop re-reads `NextDueIn` the moment the run ends,
so the wake could only add a redundant tick here and a redundant enumerate on every other container.
Waking a different job from inside a run goes through as usual.

Two guards keep `Enumerate` and `NextDueIn` off the database's hot list. A tick that enumerated
items and won none of their claims (every item was in flight on another container or backing off
after a failure) sleeps the full `Interval` instead of asking `NextDueIn`, which would still answer
"now" for those same items and re-enumerate them at the 250 ms floor for as long as they stayed due;
a wake still cuts that sleep short, so new work is not delayed. And an `Enumerate` or `NextDueIn`
slower than a second, or an `Enumerate` returning more than 10k items, logs a warning naming the
job, since both run on every container on every tick and on every wake.

### Redis

A `FanOutCron<TItem>` needs Redis for its item claims: a worker refuses to start when
`REDIS_CONNECTION_STRING` is empty and a fan out job is present, logging the job's name and exiting
with code 1. Every other job uses Redis for its slot claim when it is configured and runs on its own
timer when it is not, so a project without Redis keeps working; the worker logs a warning at startup
in that case, since a second container would then run every job too. Every key the cron machinery
writes lives under `{REDIS_PREFIX_KEY}:cron:` (or just `cron:` with no prefix configured).

### Status and history

With Redis configured, every job's latest run and its past runs are recorded there, so a web
instance on the same Redis can serve them, on an admin page for instance. The runner writes the
job's kind and schedule when its loop starts, the next run time each time it decides one, and each
run's start, finish, outcome (`ok`, `failed` or `cancelled`), the exception type and message when it
failed, and the container that ran it. A fan out tick also records its counts (enumerated, claimed,
skipped, failed, released). A job whose loop ended (a throwing `Interval`, an expression with no
next occurrence) is marked stopped with the reason; a worker restart clears the mark.

The latest run lives in the hash `{prefix}:cron:{Job}:status`, and the run history in the stream
`{prefix}:cron:{Job}:runs`, capped at the last 10,000 runs, so it needs no pruning and the load does
not grow with time. A fan out tick that claimed nothing and failed nothing (up to four a second per
container while a job is being woken) only refreshes the hash; it is not appended to the history,
which would otherwise be all empty ticks. Recording is a no-op without Redis, and a Redis failure
while recording is logged as a warning and does not affect the run.

Read them back with `CronStatus.ListAsync()` (every job, ordered by name), `CronStatus.GetAsync(name)`
and `CronStatus.RunsAsync(name, count, before)`, which pages newest first: pass the last returned
run's `Id` as `before` for the next page. `CronJobStatus.Running` is true when the latest start has
no finish after it; a container that died mid run leaves it true until the next run, so pair it with
`LastStartedAt` against the interval.

### Crash safety

A run that throws, and a failed slot claim, are caught and logged, and the schedule
continues; the next tick happens as usual. A failure computing the next tick itself (a throwing
`Interval` getter, or a cron expression that can no longer be parsed) is different: it is logged and
that job's loop ends. Other jobs keep running, and the process needs a restart to bring the broken
job back.

### Shutdown

On `SIGTERM` or `SIGINT` the worker stops scheduling new runs, logs "waiting for N running jobs" for
whichever jobs are mid-run, waits for them to finish, and only then exits. Set the container
`stop_grace_period` (Docker Compose, or `terminationGracePeriodSeconds` on Kubernetes) to at least
the duration of the slowest job, or the runtime will kill the container mid-run before the drain
finishes.

## Logging

`LOG_LEVEL` sets Serilog's minimum level: `Verbose`, `Debug`, `Information`, `Warning`, `Error` or
`Fatal`, in any case. Unset means `Debug`, the level every release so far has logged at. A value
that is not one of those also means `Debug`, and a warning naming it is the first line logged, so a
typo cannot leave production at Debug unnoticed. `Warning` is the usual production choice: it
silences the per-query and per-request Information lines.

## Releasing

`JuegoFramework` publishes to nuget.org automatically via GitHub Actions
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) —
no long-lived API key. The workflow ([`.github/workflows/publish.yml`](.github/workflows/publish.yml))
exchanges a short-lived GitHub OIDC token for a 1-hour nuget.org key and pushes the package.

### Cut a release

```bash
bash bump-version.sh                 # bumps <Version> in JuegoFramework.csproj, commits, tags vX.Y.Z
git push origin main                 # push the bump commit
git push origin v<new-version>       # pushing the tag triggers publish.yml → publishes to nuget.org
```

The workflow packs the `<Version>` from the csproj and **fails fast if the `vX.Y.Z` tag
doesn't match it**, so the tag and package version can't drift.

### One-time setup (already done; documented for reference)

- **nuget.org → Trusted Publishing → Create policy:**
  - Package owner: `JuegoStudios`
  - Repository Owner: `juegostudios-repo`, Repository: `csharp-framework`
  - Workflow File: `publish.yml`, Environment: *(empty)*
- **GitHub repo → Settings → Secrets and variables → Actions:**
  - `NUGET_USER` = the **personal nuget.org username of the policy creator** (e.g. `usernamejuego`),
    **not** the package owner (`JuegoStudios`) and **not** an email. The token exchange looks the
    policy up by its creator's user handle; using the owner org name returns
    `No matching trust policy owned by user ...`.

> Publishing under a different nuget.org account? Create a new policy under **that**
> account and set `NUGET_USER` to that account's personal handle.

### Templates package

`JuegoFramework.Templates` still uses the legacy manual script
[`build-and-publish-template.sh`](build-and-publish-template.sh) with a long-lived
`NUGET_API_KEY`. It versions independently of the core library and is not yet on trusted
publishing — migrating it means adding a second workflow + nuget.org policy on a distinct
tag pattern (e.g. `templates-v*`).

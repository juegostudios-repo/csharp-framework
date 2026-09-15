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

- **`Cron`**, a fixed interval job. Override `Interval` and `Run`.
- **`ScheduledCron`**, a Cronos cron expression with seconds. Override `Expression` and `Run`.
- **`FanOutCron<TItem>`**, a fixed interval job spread across the fleet item by item. Override
  `Enumerate` and `Process` instead of `Run`.

A long running job can exit early by observing the `protected CancellationToken Stopping` property
on its base class, either by passing it to the work it awaits or by polling it between items.

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
process it again. `Process` must therefore be safe to repeat. Set `HoldClaimAfterSuccess` to true
when the lease itself must guarantee one pass per cycle, which is what a job whose `Enumerate`
cannot tell "done" from "still needs work" wants: the claim is then kept until the lease lapses. A per item failure is logged and keeps that
item's claim, so a broken item backs off for `ItemLease` instead of retrying in a tight loop. An
item whose `Process` outlives `ItemLease` logs a warning, since another container may have taken it
meanwhile. `ItemKey` defaults to the item's `ToString`, which is right for a number, a string or a
Guid; for a class it is the type name, so override it, or every item shares one claim (duplicate
keys in a tick are logged as an error). With `Concurrency` above one, `Process` runs on several
threads at once and must not share mutable state between items. Above
roughly 100k items per tick, shard the job (for example by a key range) instead of enumerating
everything in one job.

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
fan out job is ignored with a Debug log line.

### Redis

A `FanOutCron<TItem>` needs Redis for its item claims: a worker refuses to start when
`REDIS_CONNECTION_STRING` is empty and a fan out job is present, logging the job's name and exiting
with code 1. Every other job uses Redis for its slot claim when it is configured and runs on its own
timer when it is not, so a project without Redis keeps working; the worker logs a warning at startup
in that case, since a second container would then run every job too. Every key the cron machinery
writes lives under `{REDIS_PREFIX_KEY}:cron:` (or just `cron:` with no prefix configured).

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

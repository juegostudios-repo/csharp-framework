# Changelog

## 1.0.29 (2026-09-15)

### Dapper type maps are registered once, not per query

`SQLManager` re-registered each entity's Dapper type map on every query. Dapper's `SetTypeMap`
purges its whole query cache each time it is called, so every query threw away every compiled row
mapper and Dapper rebuilt one on the next call. The map depends only on the type's attributes, so
it is now registered once per type. 1.0.28 shipped this behind `SQLMANAGER_CACHE_TYPEMAP=1` as a
benchmark toggle; the toggle is gone and the once-per-type path is the only one.

### `LOG_LEVEL` is documented, case-insensitive, and loud about typos

1.0.28 added `LOG_LEVEL` without documenting it, matched it case-sensitively, and fell back to
Debug in silence on anything it did not recognise, so `LOG_LEVEL=warning` left production at Debug
with nothing in the log to say so. It now accepts any case, and an unrecognised value logs a warning
naming it as the first line out. `LOG_SINK=none`, which dropped the console sink for a benchmark and
was never documented, is removed: `LOG_LEVEL=Fatal` gets a benchmark close enough to silent.

## 1.0.28 (2026-09-15)

### Cron jobs survive a crash and drain cleanly on shutdown

A cron job that threw used to take its own schedule down with it: the exception unwound the timer
callback and nothing scheduled that job again. `Run` is now caught by the shared runner, logged, and
the schedule continues on its next tick as if nothing happened. The same net now covers a job whose
`Interval` or `Expression` getter throws, or whose next occurrence cannot be computed: the failure is
logged and only that job's loop ends, instead of the exception escaping unobserved.

Shutdown changes too. `CronJobService.Start` now traps `SIGTERM` and `SIGINT`, stops handing out new
ticks, logs how many jobs are still mid-run, and waits for them to finish before the process exits.
Before this, a container orchestrator's stop signal could kill the process mid-run, mid-write. A
container running cron jobs should set `stop_grace_period` (Compose) or
`terminationGracePeriodSeconds` (Kubernetes) to at least the slowest job's typical run time so the
drain has time to finish.

### One shared runner, and discovery no longer crashes on an abstract or generic base

`Cron` and `ScheduledCron` used to schedule themselves independently, each with its own timing and
error handling to keep in sync. Both now delegate to one internal `CronRunner`, so a fix or a new
capability (the crash safety above, or the timing changes below) lands once for both job kinds
instead of twice.

Discovery also got safer. It already matched any transitive subclass of `Cron` or `ScheduledCron`
(`Type.IsSubclassOf`), but it handed every match straight to `Activator.CreateInstance`. An abstract
intermediate base class between a job and its cron base, or a concrete open generic like
`FanOutCron<TItem>` itself, matched that check and crashed the entire worker at startup
("Cannot create an abstract class"). Discovery now walks the base type chain explicitly and skips
abstract types and open generic types before ever constructing one, so a shared intermediate base
can sit between a job and its cron base class without taking the worker down. This is what lets
`FanOutCron<TItem>` (below) exist as a generic base with concrete jobs under it.

### One container per tick, whenever Redis is configured

A cron job means "at this time, do this once". Before, every container ran every job, so a fleet of
two CRON containers did everything twice. Now, when `REDIS_CONNECTION_STRING` is set, a `Cron`
aligns its ticks to the clock and every `Cron` and `ScheduledCron` claims each tick's slot in Redis,
so exactly one container runs it and the rest skip. A run that outlives its tick keeps a running
lock that skips the next tick elsewhere rather than starting a second copy. There is nothing to opt
into per job. The one exception is `FanOutCron<TItem>` (below), which ticks on every container on
purpose and splits its work through per item claims instead. Without Redis a job runs on its own
timer as before, which is the single container case, so a project with no Redis is unaffected; the
worker logs a warning at startup in that case, since a second container would then run every job.

### `NextDueIn` and `CronWake`, waking a fan out job between ticks

A job whose real work arrives irregularly used to be stuck picking between a short interval that
polls mostly for nothing, or a long one that leaves work waiting. `FanOutCron<TItem>.NextDueIn` lets a job
report how long until it actually has work, computed however it likes (typically against the
database clock), and the runner sleeps for that instead of the full interval, clamped between 250ms
and `Interval` so a bad answer cannot spin the loop or blow past the interval either.
`CronWake.PublishAsync<TJob>()` complements it: any process can publish a wake for a job, and every
worker running that job cuts its current sleep short and checks again immediately. The typed
overload only accepts a fan out job, so a wake for a job that cannot be woken is a compile error;
`PublishAsync(string)` remains for a publisher that cannot reference the job type. A wake is a hint,
never a guarantee, so the job's own schedule is still what keeps it correct.

### `FanOutCron<TItem>`, spreading one tick across the fleet

Some jobs have per-item work that scales with data, not with the tick interval, e.g. one row per
active user. `FanOutCron<TItem>` lets a job implement `Enumerate` and `Process` instead of `Run`:
every container enumerates the same items, each item is claimed once in Redis, and only the winners
are processed, with `Concurrency` in parallel. Adding containers adds throughput instead of
duplicating work. The contract is at least once, so `Process` must be safe to repeat; see the README
for the exact guarantees and the `HoldClaimAfterSuccess` knob. Two things are logged so the contract
is not silently broken: items whose `ItemKey` repeats another's in one tick (an error, since only one
of them is processed), and an item whose `Process` outlives `ItemLease` (a warning, since another
container may have taken it meanwhile).

### Redis backed tests that skip without a Redis to talk to

The slot claim, `NextDueIn`/`CronWake`, and fan-out tests need a real Redis to prove the
claim, lock, and wake behaviour under contention; a unit test with a fake cannot exercise the actual
Lua scripts and expiries. They now live behind a custom fact attribute that skips them, rather than
failing, when `REDIS_CONNECTION_STRING` is not set, so the rest of the suite still runs green on a
machine with no Redis.

### Behaviour changes

A few existing behaviours changed as a side effect of the above, worth knowing if you have jobs
already running on 1.0.27 or earlier:

- A `ScheduledCron` whose `Expression` has no next occurrence (Cronos returns null, for instance a
  date that never comes) used to throw `InvalidOperationException` out of its loop. It now logs an
  error and the job stays idle. An `Expression` that does not parse still stops the worker at
  startup, as it always did, but the log line now names the job instead of showing a bare
  `TargetInvocationException`.
- The "next run at" log line moved from Information to Debug, since an interval job can tick every
  few seconds and would otherwise flood Information-level logs.
- A `Cron` with a non-positive `Interval` used to throw at startup (`System.Timers.Timer` rejects it).
  It is now logged as an error and the job is skipped, so one broken job's interval no longer takes
  the whole worker down.
- `SingleExecutionAsyncTimer` and `ScheduledCron.RunScheduledTask()` are gone; `CronRunner` replaced
  both and no consumer referenced either.
- `CronJobService.Start` now exits the process itself once shutdown has drained, rather than relying
  on the host to kill it after the last run finishes.
- Two cron jobs with the same class name in different namespaces now stop the worker at startup,
  since slot claims, item claims and wakes are all keyed on the bare class name.
- With `REDIS_CONNECTION_STRING` set, a `Cron`'s ticks are now aligned to the clock and every tick
  costs one Redis `SET`. A single container behaves the same otherwise; several containers now run
  each job once per tick instead of once per container.

## 1.0.25 (2026-06-11)

### Generalized cross-instance fan-out routing (`InstanceFanout<T>`)

Some fan-outs need to process each recipient *before* the socket write — e.g. coalescing a high-frequency feed so each connection gets at most one batched message per window. `WebSocketBackplane` gives an application nowhere to do that: it delivers straight to *sockets*, with no project seam between "event produced" and "bytes written." So an app that needs it had to re-implement the backplane's instance routing in its own code: `GroupByInstance`, a parallel `…:inst:{id}` channel, a subscriber, and — easy to forget — the non-cluster special case (outside `SERVER_CLUSTER` the framework mints **bare** connection ids with no `{instanceId}:` prefix, which `GroupByInstance` silently drops). Forgetting that last part means the feed delivers nothing in single-instance mode while still working in cluster mode, so tests on a cluster harness pass.

`InstanceFanout<T>` moves that routing — and the websocket-mode awareness — into the framework, once:

- Construct with a channel base (e.g. `"march:inst"`) and a local sink `Action<string /*connectionId*/, T /*payload*/>`.
- `RouteAsync(connectionIds, payload)` hands the payload to the sink on whichever instance owns each connection. Non-cluster modes: every connection is local, so the sink runs inline for all. `SERVER_CLUSTER`: locals run inline, remotes are batched per owning instance and forwarded over Redis, and the owning instance's subscriber runs the **same** sink on arrival.
- `StartAsync()` subscribes this instance's channel (cluster-only; idempotent). Call once at startup.

The payload `T` is serialized only when it crosses the wire to a remote instance; the local path passes the in-memory instance straight to the sink. Application code supplies only a payload and a sink — it never inspects `USE_WEBSOCKET_SYSTEM`, the connection-id format, or the channel.

### Pluggable inline WebSocket message handler

The WebSocket receive loop used to special-case exactly one message inline — the client ping (replied with a pong, no routing). Every other inbound message went through the full routing path (`WebSocketHelper` → `RouteExecutor`), which spins up a fresh DI scope and a new `DefaultHttpContext` per message and re-runs the auth filters. In `JWT_SQL` mode that means a database lookup on *every* routed socket message, with no per-connection auth cache.

That is fine for ordinary request/response actions, but it is the wrong shape for high-frequency, fire-and-forget signals a client sends over the same socket (e.g. a viewport/area-of-interest heartbeat). At scale those would generate one auth DB lookup per message.

This release adds an optional, additive hook so an application can handle such messages inline, using the identity already established at connect time:

- New `WebSocketService.InlineMessageHandler` — a `Func<string /*connectionId*/, string /*rawMessage*/, Task<bool>>?`, defaulting to `null`. Set it once at startup.
- It is invoked in the receive loop **after** the ping short-circuit and **before** the routing path. Return `true` to signal the message was consumed inline — the loop then does NOT route it and runs no per-message auth. Return `false` to fall through to normal routing, unchanged.
- The handler receives the connect-time `connectionId` (already authenticated via `IWebSocketHandler.ConnectSocket`), so it never triggers `RouteExecutor`'s per-message `UserAuth` DB lookup.

Fully backward compatible: when no handler is registered, the receive loop behaves exactly as before — ping is still answered inline, and every other message still routes through `RouteExecutor` unchanged.

## 1.0.23 (2026-05-14)

### SQLManager.Transaction now works the way you'd expect

A transaction is a block of database work that either all succeeds together or all gets undone together. Classic example: a bank transfer. You take money out of one account and put it in another, and if the second write fails you don't want the first one to stick. `SQLManager.Transaction(...)` is the framework's helper for wrapping work in that guarantee. This release fixes three real issues with it.

#### Problem 1: You can't safely call a transactional function from inside another transaction

Before: if function A used `Transaction(...)` and the work inside called function B, which also used `Transaction(...)`, the inner call quietly opened a separate database connection, committed its writes independently, and corrupted the framework's internal state so the next database call from A crashed. Nesting was both useless (no shared atomicity) and dangerous (cascading crash).

After: nesting works. The inner call joins the outer transaction using a database savepoint. If the inner work fails, only the inner writes roll back; the outer can catch the error and continue or fail. If the outer fails, everything rolls back together. The internal state corruption is gone as part of the same fix.

What this unlocks in practice: helper functions that need their own atomicity ("place this user", "credit this refund", "post this comment with attachments") can be called from larger workflows ("sign up flow", "checkout flow", "publish flow") and the right thing happens whether the inner or the outer fails.

#### Problem 2: When a transaction fails, you couldn't see why

Before: every failure inside a `Transaction(...)` lambda came back as a generic `Exception("Transaction failed")`. The original message, type, and stack trace were dropped. Debugging required adding log statements inside every transaction lambda.

After: the original exception propagates unchanged. Logs and error tracking show what actually went wrong (the SQL message, the constraint violation, the null reference, whatever it was).

**Breaking change:** if you have code that catches the exception from `Transaction(...)` and checks `ex.Message == "Transaction failed"`, update it to handle the inner exception's type or message. Code that catches `Exception` and just logs needs no change and starts getting better information for free.

#### Problem 3: Quality-of-life gaps

Three small additions:

- `Transaction<T>(Func<Task<T>>)` — return a value from a transactional block instead of capturing into an outer variable.
- Optional `IsolationLevel` parameter — pick a stricter or looser isolation level when you have a reason to. Default behavior unchanged.
- Optional `CancellationToken` parameter — let an upstream timeout or shutdown cancel a long-running transaction.

#### Edge case to know about

If your `Transaction(...)` lambda had a `catch` block that did its own database writes (cleanup work) and then swallowed the exception, those cleanup writes used to leak onto a separate connection and commit independently of the rollback. They now run on the rolling-back transaction and get rolled back with everything else, which is almost always what you actually wanted. If you genuinely need the cleanup to commit on its own, do it outside the `Transaction(...)` block instead.

### WebSocket: response event when request includes a requestId

If a WebSocket message comes in carrying a `requestId`, the framework now sends a response event back over the WebSocket containing the controller's return value, formatted the same way as an HTTP response. Lets clients correlate WebSocket replies to their original requests for request/response style messaging over a persistent socket.

New helper: `WebSocketResponseHelper.FormatResponse(IActionResult, localizer)` normalizes a controller's `IActionResult` into the standard `ReturnResponse` shape before dispatch. The two WebSocket controllers (`AWSWebSocketController`, `AzureWebSocketController`) and `WebSocketService` were updated to call into it. `SocketEventDto` and `WebSocketHelper` gained the supporting plumbing.

This was originally landed in #9 on 2025-08-05 but never released; this is its first appearance on NuGet.

## Other repo changes since 1.0.22 (not in this NuGet package)

These changes landed on `main` between 2025-08-01 and the present but live outside the `JuegoFramework` NuGet package. Listing them so it's clear what's in the repo vs what consumers actually get by bumping the package reference.

### Starter template (`JuegoFramework.Templates` NuGet package — currently still 1.0.0)

- `9d0b124` (2025-08-01) — unify error response messages to `UNKNOWN_ERROR` in the template's `CustomController` and `UserController`.
- `f59d6e3` (2025-08-01) — `SocketPing` task uses a logger instance instead of static `Log` calls; added example for expression-based crons.
- `d640125` (2025-08-01) — template gains a `MySqlDateTimeHandler` and refactored connection-string retrieval to fix a DateTime mapping issue.
- `671b99b` (2025-09-08) — broader template polish: extra env-example entries, `ApiLoggingMiddleware`, `WebSocketHandler`, login-service updates, two example cron tasks (`ExampleCron`, `ExampleScheduledCron`), README + docker-compose tweaks.
- `c93065d` (2025-09-09) — initialize `DeviceId` as an empty string in the template's `LoginService.ValidateAuthData` (fixes a null-ref scenario).

These will ship the next time `JuegoFramework.Templates` is published — not as part of `JuegoFramework` 1.0.23.

### CLI tools (`JuegoCliTools/cjs-tools` — separate dotnet tool)

- `482e5f7` (2025-09-08) — added JWT Updater and Project CLI tools. Ships in `JuegoCliTools/` as a standalone dotnet tool, not via the `JuegoFramework` NuGet package.

## 1.0.22 (2025-05-14)

- Add `CronNameEnricher` to format `CronName` in logs.

(No CHANGELOG existed before 1.0.23; this is a reconstructed minimal entry for the prior release. Earlier history is in git.)

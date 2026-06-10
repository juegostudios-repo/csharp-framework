# Changelog

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

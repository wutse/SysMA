# BrokerageMonitor.MailAgent — Architecture Review

> **Reviewer**: Chief Software Architect
> **Date**: 2026-04-22
> **Layer**: MailAgent (standalone process — no project references to Domain/Application)

---

## 📊 Architecture Health Score: 7.5 / 10

The MailAgent is cleanly isolated as a separate Windows process with no coupling to the main solution's Domain or Application layers. The STA thread pattern for Outlook COM Interop is architecturally correct, and COM object cleanup is thorough. The primary concerns are a costly COM object lifecycle (new `Application` instance per poll), a sync-over-async anti-pattern in the public API, and unbounded email body sizes in transit.

---

## ✅ Architectural Strengths

1. **Process Isolation** — The MailAgent is a fully independent `net8.0-windows` executable. It communicates with the main application exclusively via ZeroMQ (`mailrelay` topic), enforcing a hard process boundary. Changes to Domain or Application models cannot break this agent.

2. **STA Thread Architecture for COM Interop** — `OutlookMailReader` creates a dedicated `Thread` with `ApartmentState.STA` and routes all Outlook COM calls through a `BlockingCollection<Action>` work queue. This is the correct pattern for Outlook COM interop in a hosted service, preventing `COMException` from multi-threaded access.

3. **COM Object Cleanup** — Every Outlook COM object (`Application`, `NameSpace`, `MAPIFolder`, `Items`, `MailItem`) is released via `Marshal.ReleaseComObject()` in `finally` blocks or within the processing loop, preventing COM reference leaks.

4. **`PeriodicTimer` for Polling** — `MailRelayWorker` uses `PeriodicTimer` (the modern .NET 6+ alternative to `Timer`), which correctly skips missed ticks rather than queuing them up, and integrates cleanly with `CancellationToken` for graceful shutdown.

5. **Bounded `BlockingCollection`** — The STA work queue has `boundedCapacity: 64`, preventing unbounded memory growth if the polling loop produces work faster than the STA thread can consume it.

6. **`IDisposable` Guarded with `ObjectDisposedException.ThrowIf`** — `OutlookMailReader.ReadUnreadMails()` and `ZeroMQMailPublisher.Publish()` both guard against post-dispose usage.

7. **PUB Socket Reconnection** — `ZeroMQMailPublisher` disposes a broken socket and re-creates it on the next `Publish()` call, ensuring the agent self-heals after broker disconnections.

---

## ⚠️ Critical Violations

### 1. New Outlook `Application` Instance Created on Every Poll

`ReadUnreadMailsOnSta()` creates `new MSOutlook.Application()` on every invocation, including a `Logon` / `Logoff` cycle. This is expensive and violates the recommended Outlook COM Interop lifecycle, which calls for a single, long-lived `Application` instance for the process lifetime.

**Impact**: On each poll cycle, Outlook must re-initialize its COM server context. This can cause visible Outlook UI flickering, slow polls, and increased risk of `MarshalDirectiveException` or COM RPC failures under load.

```csharp
// OutlookMailReader.cs — per-poll, inside ReadUnreadMailsOnSta()
app = new MSOutlook.Application(); // ❌ created every poll
ns = app.GetNamespace("MAPI");
ns.Logon(Missing.Value, Missing.Value, false, false);
// ...
ns.Logoff(); // ❌ torn down every poll
```

---

### 2. Sync-Over-Async in `ReadUnreadMails()`

```csharp
public IReadOnlyList<RawMailItem> ReadUnreadMails()
{
    // ...
    return tcs.Task.GetAwaiter().GetResult(); // ❌ sync-over-async
}
```

`GetAwaiter().GetResult()` blocks the calling thread (the `MailRelayWorker`'s async context). While this is not a deadlock risk here (the STA thread and calling thread are different), it ties up a thread-pool thread for the full duration of the COM call and prevents the caller from propagating cancellation.

---

### 3. Unbounded Email Body in ZeroMQ Frame

Email bodies are passed to `ZeroMQMailPublisher.Publish()` without any size validation or truncation:

```csharp
var json = JsonSerializer.Serialize(message, JsonOptions); // body included in full
_publisher.Publish(json);
```

**Impact**: A large HTML newsletter or an attachment-embedded email could produce a multi-MB ZeroMQ frame, causing memory pressure on both the MailAgent and the receiving Web process. NetMQ has no built-in frame size limits.

---

### 4. `MailRelayWorker.PollAndPublishAsync` Is Not Truly Async

```csharp
private Task PollAndPublishAsync(CancellationToken ct)
{
    // ...
    foreach (var mail in mails)
    {
        // synchronous publish
        _publisher.Publish(json);
    }
    return Task.CompletedTask; // ❌ pretends to be async
}
```

The method signature returns `Task` but never awaits anything. This is misleading to readers and prevents `await`-based cancellation inside the loop body.

---

## 💡 Refactoring Suggestions

1. **Reuse the Outlook `Application` instance** — Initialize `MSOutlook.Application` and `NameSpace` once in the `OutlookMailReader` constructor (on the STA thread), keep them alive, and dispose them in `Dispose()`. Call `Logon` once at startup and `Logoff` only at shutdown.

2. **Make `ReadUnreadMails` truly async or return `ValueTask`** — Accept a `CancellationToken` and link it to the `TaskCompletionSource`. The STA thread should call `tcs.TrySetCanceled(ct)` if the token fires.

3. **Truncate email body before publishing** — Apply a maximum body length (e.g., 64 KB) and truncate with an indicator, or hash the full body and store only a preview.

4. **Convert `PollAndPublishAsync` return type to `void` or make it genuinely async** — If publish is synchronous, rename to `PollAndPublish()`. If publish is to become async (e.g., fire-and-forget queue), implement accordingly.

---

## 📝 Implementation Examples

### Before — New COM Instance Per Poll

```csharp
// OutlookMailReader.cs (current)
private IReadOnlyList<RawMailItem> ReadUnreadMailsOnSta()
{
    MSOutlook.Application? app = null;
    MSOutlook.NameSpace? ns = null;
    try
    {
        app = new MSOutlook.Application();     // ❌ per-poll
        ns = app.GetNamespace("MAPI");
        ns.Logon(...);                          // ❌ per-poll
```

### After — Single Lifetime COM Instance

```csharp
// OutlookMailReader.cs (proposed)
private MSOutlook.Application? _app;
private MSOutlook.NameSpace? _ns;

// Called once on the STA thread during initialization:
private void InitializeOutlookOnSta()
{
    _app = new MSOutlook.Application();   // ✅ once
    _ns  = _app.GetNamespace("MAPI");
    _ns.Logon(Missing.Value, Missing.Value, false, false); // ✅ once
    _logger.LogInformation("Outlook session initialized.");
}

private IReadOnlyList<RawMailItem> ReadUnreadMailsOnSta()
{
    if (_app is null) InitializeOutlookOnSta();
    // use _ns directly — no logon/logoff cycle ✅
}

public void Dispose()
{
    // Queue logoff to the STA thread
    _workQueue.Add(() =>
    {
        if (_ns is not null) { try { _ns.Logoff(); } catch { } }
        ReleaseIfNotNull(_ns);
        ReleaseIfNotNull(_app);
    });
    _workQueue.CompleteAdding();
    _workQueue.Dispose();
    _disposed = true;
}
```

---

### Before — Unbounded Body

```csharp
var message = new MailRelayMessage(..., Body: mail.Body, ...); // ❌ uncapped size
```

### After — Body Capped at 64 KB

```csharp
private const int MaxBodyBytes = 65_536;

var body = mail.Body ?? string.Empty;
if (System.Text.Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
{
    body = body[..Math.Min(body.Length, 8_000)] + "\n[truncated]";
    _logger.LogWarning("Mail body truncated to stay within ZeroMQ frame limit.");
}
var message = new MailRelayMessage(..., Body: body, ...); // ✅ bounded
```

---

```mermaid
%% MailAgent — Internal Architecture
graph TD
    subgraph "MailAgent Process (STA-aware)"
        MRW["MailRelayWorker<br/>(BackgroundService)"]
        OMR["OutlookMailReader<br/>(Singleton, IDisposable)"]
        STA["Dedicated STA Thread<br/>(BlockingCollection queue)"]
        COM["Outlook COM Objects<br/>(Application / NameSpace / Folder)"]
        ZMQ["ZeroMQMailPublisher<br/>(Singleton, PUB socket)"]
    end

    subgraph "External"
        OLK["Outlook Mailbox"]
        ZMQB["ZeroMQ Broker<br/>(XPUB/XSUB)"]
    end

    MRW -->|"ReadUnreadMails()"| OMR
    OMR -->|"enqueue work"| STA
    STA -->|"COM calls"| COM
    COM -->|"MAPI"| OLK
    STA -->|"return RawMailItem[]"| OMR
    OMR -->|"return to caller"| MRW
    MRW -->|"Publish(json)"| ZMQ
    ZMQ -->|"PUB frame: mailrelay / json"| ZMQB
```

> **Design Intent**: All Outlook COM access is channelled through a single dedicated STA thread to satisfy Outlook's apartment model requirements. `MailRelayWorker` and `ZeroMQMailPublisher` are free-threaded; only `OutlookMailReader`'s internal implementation is STA-constrained.

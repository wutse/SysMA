# BrokerageMonitor.MailAgent — Review Summary

> **Last Review**: 2026-05-03 | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 9.0 / 10**

The MailAgent is architecturally sound as an independently deployable `net8.0-windows` process. The STA thread architecture for Outlook COM interop is correctly implemented with a `BlockingCollection<Action>` work queue and `Marshal.ReleaseComObject` cleanup in all paths. The long-lived `_outlookApp` / `_outlookNs` session management is confirmed in place. No critical or medium violations exist. The two remaining items are accepted as by-design trade-offs given the COM interop constraints, but have actionable improvement paths.

---

## 🔧 Pending Action Items

1. **(ADVISORY)** `ReadUnreadMails()` blocks its caller via `tcs.Task.GetAwaiter().GetResult()` — an inherent consequence of the STA bridge pattern. Accepted by design, but cancellation from the caller cannot propagate into the STA work item. Improvement: pass the `CancellationToken` into the `TaskCompletionSource` and have the STA thread call `tcs.TrySetCanceled(ct)` when the token fires.

2. **(ADVISORY)** `PollAndPublishAsync` returns `Task.CompletedTask` with no `await` points — a synchronous method with a misleading `Async` suffix. Rename to `PollAndPublish()` returning `void`, or make genuinely async when the publish path moves to an async queue.

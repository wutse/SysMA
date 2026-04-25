using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using BrokerageMonitor.MailAgent.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOutlook = Microsoft.Office.Interop.Outlook;

namespace BrokerageMonitor.MailAgent.Outlook;

/// <summary>
/// Reads unread mail items from an Outlook mailbox folder via COM Interop.
/// All COM operations are executed on a dedicated STA thread to satisfy the
/// Outlook Application object's Single-Threaded Apartment requirement.
/// Marks each mail item as read after extraction.
/// Exceptions are caught, logged at ERROR level, and an empty list is returned so
/// that the polling loop continues uninterrupted.
/// FR-048.
/// </summary>
public sealed class OutlookMailReader : IOutlookMailReader, IDisposable
{
    private readonly string _folderName;
    private readonly ILogger<OutlookMailReader> _logger;

    // STA thread that owns the Outlook COM objects for their entire lifetime.
    private readonly Thread _staThread;

    // Synchronous channel: work items are queued to the STA thread.
    private readonly BlockingCollection<Action> _workQueue = new(boundedCapacity: 64);
    private bool _disposed;

    public OutlookMailReader(
        IOptions<MailAgentOptions> options,
        ILogger<OutlookMailReader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _folderName = options.Value.MailboxFolder;
        _logger = logger;

        // Start a dedicated STA background thread that processes work items.
        _staThread = new Thread(RunStaLoop)
        {
            IsBackground = true,
            Name = "OutlookMailReader-STA"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    /// <inheritdoc/>
    public IReadOnlyList<RawMailItem> ReadUnreadMails()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tcs = new TaskCompletionSource<IReadOnlyList<RawMailItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _workQueue.Add(() =>
        {
            try
            {
                tcs.SetResult(ReadUnreadMailsOnSta());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        // Block the calling thread until the STA work item completes.
        return tcs.Task.GetAwaiter().GetResult();
    }

    // -------------------------------------------------------------------------
    // STA thread loop
    // -------------------------------------------------------------------------

    private void RunStaLoop()
    {
        foreach (var workItem in _workQueue.GetConsumingEnumerable())
        {
            workItem();
        }
    }

    // COM objects reused across polls — created once on the STA thread.
    private MSOutlook.Application? _outlookApp;
    private MSOutlook.NameSpace? _outlookNs;

    // -------------------------------------------------------------------------
    // Core Outlook COM logic — runs exclusively on the STA thread
    // -------------------------------------------------------------------------

    private IReadOnlyList<RawMailItem> ReadUnreadMailsOnSta()
    {
        MSOutlook.MAPIFolder? folder = null;
        MSOutlook.Items? items = null;

        try
        {
            EnsureOutlookSession();

            folder = FindFolder(_outlookNs!, _folderName);
            if (folder is null)
            {
                _logger.LogError(
                    "Outlook folder '{FolderName}' was not found in the default store.", _folderName);
                return [];
            }

            // Restrict to unread items only to minimise COM marshalling cost.
            items = folder.Items;
            var restricted = items.Restrict("[UnRead] = True");

            var results = new List<RawMailItem>();
            // Iterate in reverse so that marking-as-read does not shift indices.
            for (int i = restricted.Count; i >= 1; i--)
            {
                if (restricted[i] is not MSOutlook.MailItem mail)
                    continue;

                try
                {
                    var item = ExtractMailItem(mail);
                    mail.UnRead = false;
                    mail.Save();
                    results.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read or mark mail item as read — skipping.");
                }
                finally
                {
                    Marshal.ReleaseComObject(mail);
                }
            }

            Marshal.ReleaseComObject(restricted);
            return results.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outlook COM error while reading mailbox folder '{FolderName}'.", _folderName);
            // Tear down the cached session so the next poll attempts a fresh connection.
            ReleaseOutlookSession();
            return [];
        }
        finally
        {
            ReleaseIfNotNull(items);
            ReleaseIfNotNull(folder);
        }
    }

    /// <summary>
    /// Creates the <see cref="MSOutlook.Application"/> and <see cref="MSOutlook.NameSpace"/>
    /// once per reader lifetime (lazy, STA-thread only).
    /// </summary>
    private void EnsureOutlookSession()
    {
        if (_outlookApp is not null && _outlookNs is not null)
            return;

        _outlookApp = new MSOutlook.Application();
        _outlookNs = _outlookApp.GetNamespace("MAPI");
        _outlookNs.Logon(Missing.Value, Missing.Value, false, false);
        _logger.LogInformation("OutlookMailReader: COM session initialised.");
    }

    /// <summary>
    /// Releases the cached COM session. Called on error or dispose.
    /// </summary>
    private void ReleaseOutlookSession()
    {
        if (_outlookNs is not null)
        {
            try { _outlookNs.Logoff(); } catch { /* best-effort */ }
            Marshal.ReleaseComObject(_outlookNs);
            _outlookNs = null;
        }
        if (_outlookApp is not null)
        {
            Marshal.ReleaseComObject(_outlookApp);
            _outlookApp = null;
        }
    }

    private static RawMailItem ExtractMailItem(MSOutlook.MailItem mail) =>
        new(
            From: mail.SenderEmailAddress ?? mail.SenderName ?? string.Empty,
            Subject: mail.Subject ?? string.Empty,
            Body: mail.Body ?? string.Empty,
            ReceivedAt: mail.ReceivedTime.ToUniversalTime());

    /// <summary>
    /// Searches the default store's top-level folders for a folder matching <paramref name="name"/>.
    /// Returns <see langword="null"/> if not found.
    /// </summary>
    private static MSOutlook.MAPIFolder? FindFolder(MSOutlook.NameSpace ns, string name)
    {
        var store = ns.DefaultStore;
        var root = store.GetRootFolder();
        foreach (MSOutlook.MAPIFolder sub in root.Folders)
        {
            if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase))
                return sub;
            Marshal.ReleaseComObject(sub);
        }
        Marshal.ReleaseComObject(root);
        Marshal.ReleaseComObject(store);
        return null;
    }

    private static void ReleaseIfNotNull(object? obj)
    {
        if (obj is not null)
            Marshal.ReleaseComObject(obj);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workQueue.CompleteAdding();
        // Release COM session on the STA thread (synchronous shutdown).
        if (!_workQueue.IsAddingCompleted)
            _workQueue.Add(ReleaseOutlookSession);
        else
            ReleaseOutlookSession();
        _workQueue.Dispose();
    }
}

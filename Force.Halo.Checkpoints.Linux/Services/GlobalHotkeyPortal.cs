using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus;

namespace Force.Halo.Checkpoints.Linux.Services;

internal sealed class GlobalHotkeyPortal : IAsyncDisposable
{
    public const string ShortcutId = "force-checkpoint";
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath PortalPath = new("/org/freedesktop/portal/desktop");

    private readonly Connection _connection;
    private readonly IGlobalShortcuts _portal;
    private ObjectPath? _sessionHandle;
    private IDisposable? _activatedSubscription;

    private GlobalHotkeyPortal(Connection connection, IGlobalShortcuts portal)
    {
        _connection = connection;
        _portal = portal;
    }

    public event Action? Activated;

    public static async Task<GlobalHotkeyPortal?> TryCreateAsync()
    {
        try
        {
            var options = new ClientConnectionOptions(Address.Session)
            {
                SynchronizationContext = new SynchronizationContext()
            };
            var connection = new Connection(options);
            await connection.ConnectAsync();
            var portal = connection.CreateProxy<IGlobalShortcuts>(PortalService, PortalPath);
            return new GlobalHotkeyPortal(connection, portal);
        }
        catch
        {
            return null;
        }
    }

    public async Task<BindResult> RebindAsync(string description, string? preferredTrigger, string parentWindow)
    {
        await CloseSessionAsync();

        var sessionResult = await CreateSessionAsync();
        if (!sessionResult.Success)
        {
            return BindResult.Fail(sessionResult.Message ?? "Failed to create global shortcut session.");
        }

        if (_sessionHandle is null)
        {
            return BindResult.Fail("Global shortcut session handle missing.");
        }

        var shortcutOptions = new Dictionary<string, object>
        {
            ["description"] = description
        };

        if (!string.IsNullOrWhiteSpace(preferredTrigger))
        {
            shortcutOptions["preferred_trigger"] = preferredTrigger!;
        }

        var shortcuts = new (string, IDictionary<string, object>)[]
        {
            (ShortcutId, shortcutOptions)
        };

        ObjectPath request = await _portal.BindShortcutsAsync(_sessionHandle.Value, shortcuts, parentWindow,
            new Dictionary<string, object>());

        var (response, results) = await WaitForResponseAsync(request, CancellationToken.None);
        if (response != 0)
        {
            return BindResult.Fail(response == 1
                ? "Global shortcut setup cancelled."
                : "Global shortcut setup failed.");
        }

        string? triggerDescription = TryGetTriggerDescription(results);
        await EnsureSignalSubscriptionAsync();
        return BindResult.Ok(triggerDescription);
    }

    public async Task CloseSessionAsync()
    {
        if (_sessionHandle is null)
        {
            return;
        }

        try
        {
            var session = _connection.CreateProxy<ISession>(PortalService, _sessionHandle.Value);
            await session.CloseAsync();
        }
        catch
        {
            // Ignored: closing is best-effort.
        }

        _sessionHandle = null;
        _activatedSubscription?.Dispose();
        _activatedSubscription = null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseSessionAsync();
        }
        catch (TaskCanceledException)
        {
            // Ignore cancellation during shutdown.
        }
        catch
        {
            // Best-effort cleanup.
        }

        _connection.Dispose();
    }

    private async Task<OperationResult> CreateSessionAsync()
    {
        string handleToken = Guid.NewGuid().ToString("N");
        string sessionToken = Guid.NewGuid().ToString("N");

        var options = new Dictionary<string, object>
        {
            ["handle_token"] = handleToken,
            ["session_handle_token"] = sessionToken
        };

        ObjectPath request = await _portal.CreateSessionAsync(options);
        var (response, results) = await WaitForResponseAsync(request, CancellationToken.None);
        if (response != 0)
        {
            return OperationResult.Fail("Failed to create global shortcut session.");
        }

        if (!results.TryGetValue("session_handle", out var handleObj) || handleObj is null)
        {
            return OperationResult.Fail("Global shortcut session handle missing.");
        }

        _sessionHandle = handleObj switch
        {
            ObjectPath objectPath => objectPath,
            string handleString => new ObjectPath(handleString),
            _ => null
        };

        if (_sessionHandle is null)
        {
            return OperationResult.Fail("Global shortcut session handle missing.");
        }

        return OperationResult.Ok();
    }

    private async Task EnsureSignalSubscriptionAsync()
    {
        if (_activatedSubscription != null)
        {
            return;
        }

        _activatedSubscription = await _portal.WatchActivatedAsync((session, shortcutId, timestamp, details) =>
        {
            if (_sessionHandle.HasValue && session.Equals(_sessionHandle.Value) && shortcutId == ShortcutId)
            {
                Activated?.Invoke();
            }
        });
    }

    private async Task<(uint response, IDictionary<string, object> results)> WaitForResponseAsync(
        ObjectPath requestPath,
        CancellationToken cancellationToken)
    {
        var request = _connection.CreateProxy<IRequest>(PortalService, requestPath);
        var tcs = new TaskCompletionSource<(uint, IDictionary<string, object>)>();
        IDisposable? subscription = null;
        subscription = await request.WatchResponseAsync((response, results) =>
        {
            subscription?.Dispose();
            tcs.TrySetResult((response, results));
        });

        using var ctr = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task;
    }

    private static string? TryGetTriggerDescription(IDictionary<string, object> results)
    {
        if (!results.TryGetValue("shortcuts", out var shortcutsObj) || shortcutsObj is null)
        {
            return null;
        }

        if (shortcutsObj is ValueTuple<string, IDictionary<string, object>>[] tupleShortcuts)
        {
            return ExtractTriggerDescription(tupleShortcuts);
        }

        if (shortcutsObj is object[] objectArray)
        {
            var list = new List<(string, IDictionary<string, object>)>();
            foreach (var item in objectArray)
            {
                if (item is ValueTuple<string, IDictionary<string, object>> tuple)
                {
                    list.Add(tuple);
                }
            }

            return ExtractTriggerDescription(list.ToArray());
        }

        return null;
    }

    private static string? ExtractTriggerDescription(ValueTuple<string, IDictionary<string, object>>[] shortcuts)
    {
        foreach (var (id, values) in shortcuts)
        {
            if (id != ShortcutId)
            {
                continue;
            }

            if (values.TryGetValue("trigger_description", out var triggerObj) && triggerObj is string trigger)
            {
                return trigger;
            }
        }

        return null;
    }

    private sealed record OperationResult(bool Success, string? Message)
    {
        public static OperationResult Ok() => new(true, null);
        public static OperationResult Fail(string message) => new(false, message);
    }

    public sealed record BindResult(bool Success, string? Message, string? TriggerDescription)
    {
        public static BindResult Ok(string? triggerDescription) => new(true, null, triggerDescription);
        public static BindResult Fail(string message) => new(false, message, null);
    }
}

[DBusInterface("org.freedesktop.portal.GlobalShortcuts")]
internal interface IGlobalShortcuts : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);

    Task<ObjectPath> BindShortcutsAsync(
        ObjectPath sessionHandle,
        (string, IDictionary<string, object>)[] shortcuts,
        string parentWindow,
        IDictionary<string, object> options);

    Task<IDisposable> WatchActivatedAsync(
        Action<ObjectPath, string, ulong, IDictionary<string, object>> handler);
}

[DBusInterface("org.freedesktop.portal.Request")]
internal interface IRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<uint, IDictionary<string, object>> handler);
}

[DBusInterface("org.freedesktop.portal.Session")]
internal interface ISession : IDBusObject
{
    Task CloseAsync();
}

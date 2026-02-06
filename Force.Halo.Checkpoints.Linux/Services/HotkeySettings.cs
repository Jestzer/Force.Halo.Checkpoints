using System;

namespace Force.Halo.Checkpoints.Linux.Services;

internal sealed record HotkeySettings
{
    public string? PreferredGlobalTrigger { get; init; }
    public string? GlobalTriggerDescription { get; init; }
    public string? LocalKey { get; init; }
    public string? LocalModifiers { get; init; }
    public bool SuppressX11DisplayWarning { get; init; }
    public bool SuppressX11LibWarning { get; init; }
    public bool SuppressX11SymbolWarning { get; init; }
    public string? Note { get; init; }
    public int? ControllerButton { get; init; }
    public int? ControllerInstanceId { get; init; }
    public string? ControllerName { get; init; }

    public static HotkeySettings Empty => new();

    public HotkeySettings WithLocal(string? key, string? modifiers)
        => this with { LocalKey = key, LocalModifiers = modifiers };

    public HotkeySettings WithGlobal(string? preferredTrigger, string? triggerDescription)
        => this with { PreferredGlobalTrigger = preferredTrigger, GlobalTriggerDescription = triggerDescription };

    public HotkeySettings WithSuppressX11DisplayWarning(bool suppress)
        => this with { SuppressX11DisplayWarning = suppress };

    public HotkeySettings WithSuppressX11LibWarning(bool suppress)
        => this with { SuppressX11LibWarning = suppress };

    public HotkeySettings WithSuppressX11SymbolWarning(bool suppress)
        => this with { SuppressX11SymbolWarning = suppress };

    public HotkeySettings WithControllerBinding(int? button, int? instanceId, string? name)
        => this with
        {
            ControllerButton = button,
            ControllerInstanceId = instanceId,
            ControllerName = name
        };
}

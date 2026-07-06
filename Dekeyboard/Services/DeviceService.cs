using Dekeyboard.Configuration;
using Dekeyboard.Interop;

namespace Dekeyboard.Services;

/// <summary>
/// Identifies the internal keyboard (and optionally touchpad) and toggles them.
///
/// HOW THE INTERNAL KEYBOARD IS IDENTIFIED
/// ---------------------------------------
/// We enumerate the "Keyboard" setup class ({4D36E96B-...}). Every keyboard --
/// built-in, USB, or Bluetooth -- appears here. A device is treated as EXTERNAL when:
///
///   * its own enumerator is USB or BTH* (a plain USB / Bluetooth keyboard), OR
///   * an ANCESTOR in the device tree sits on the USB or Bluetooth bus
///     (a USB-HID keyboard shows enumerator "HID" but its parent is "USB").
///
/// The parent-tree check (DeviceControl.IsBehindUsbOrBluetooth) is what lets us keep
/// an internal I2C/HID keyboard while still ignoring an external USB-HID keyboard,
/// since both share the "HID" enumerator.
///
/// Among the remaining INTERNAL candidates we prefer, in order:
///   1. hardware id PNP0303 (the standard PS/2 keyboard),
///   2. any ACPI device,
///   3. any HID device,
///   4. the first remaining candidate.
/// The chosen device and every candidate are logged so the user can pin an explicit
/// instance id in config if auto-detection is ever wrong.
/// </summary>
public sealed class DeviceService
{
    /// <summary>How the keyboard is currently being held off.</summary>
    public enum DisableMethod { None, DeviceNode, InputBlock }

    private readonly AppConfig _config;
    private DisableMethod _method = DisableMethod.None;
    private bool _touchpadDisabled;

    public DeviceService(AppConfig config) => _config = config;

    public bool KeyboardDisabled { get; private set; }

    /// <summary>The method used by the last successful Disable() (for notifications/log).</summary>
    public DisableMethod CurrentMethod => _method;

    /// <summary>
    /// Turns input blocking on/off. Wired by App to HotkeyService.BlockPhysicalInput.
    /// Present only so DeviceService needn't reference HotkeyService directly.
    /// </summary>
    public Action<bool>? InputBlockController { get; set; }

    /// <summary>True when the device is a USB/Bluetooth keyboard, directly or via an ancestor.</summary>
    private static bool IsExternal(DeviceInfo d) =>
        d.IsUsb || d.IsBluetooth || DeviceControl.IsBehindUsbOrBluetooth(d.InstanceId);

    /// <summary>Resolve the internal keyboard instance id (config override wins).</summary>
    public DeviceInfo? ResolveInternalKeyboard()
    {
        var keyboards = DeviceControl.Enumerate(NativeMethods.GUID_DEVCLASS_KEYBOARD);

        Logger.Info($"Found {keyboards.Count} keyboard device(s):");
        foreach (var k in keyboards)
            Logger.Info($"    - {(IsExternal(k) ? "EXTERNAL" : "internal")} | {k.Description} | enum={k.Enumerator} | id={k.InstanceId}");

        // Explicit override from config.
        if (!string.IsNullOrWhiteSpace(_config.KeyboardInstanceId))
        {
            var match = keyboards.FirstOrDefault(k =>
                k.InstanceId.Equals(_config.KeyboardInstanceId, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            Logger.Warn($"Configured KeyboardInstanceId '{_config.KeyboardInstanceId}' not present; auto-detecting.");
        }

        var candidates = keyboards.Where(k => !IsExternal(k)).ToList();

        DeviceInfo? chosen =
            candidates.FirstOrDefault(HasPnp0303)                                    // classic PS/2
            ?? candidates.FirstOrDefault(k => k.Enumerator.Equals("ACPI", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(k => k.Enumerator.Equals("HID", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();

        if (chosen is not null)
            Logger.Info($"Selected internal keyboard: {chosen}");
        else
            Logger.Error("Could not identify an internal keyboard. Set KeyboardInstanceId in config.json.");

        return chosen;
    }

    /// <summary>Resolve the internal touchpad/pointing device (config override wins).</summary>
    public DeviceInfo? ResolveInternalTouchpad()
    {
        var mice = DeviceControl.Enumerate(NativeMethods.GUID_DEVCLASS_MOUSE);

        if (!string.IsNullOrWhiteSpace(_config.TouchpadInstanceId))
        {
            var match = mice.FirstOrDefault(m =>
                m.InstanceId.Equals(_config.TouchpadInstanceId, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        var candidates = mice.Where(m => !IsExternal(m)).ToList();

        // Precision touchpads describe themselves as a touch pad; prefer that.
        DeviceInfo? chosen =
            candidates.FirstOrDefault(m => m.Description.Contains("touch", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(m => m.Enumerator.Equals("ACPI", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(m => m.Enumerator.Equals("HID", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();

        if (chosen is not null) Logger.Info($"Selected internal touchpad: {chosen}");
        return chosen;
    }

    private static bool HasPnp0303(DeviceInfo d) =>
        d.HardwareIds.Any(h => h.Contains("PNP0303", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Disable the internal keyboard. Preferred path is disabling the device node; if
    /// the device isn't disableable (common on convertibles) or the call fails, fall
    /// back to blocking hardware keystrokes with the low-level hook.
    /// </summary>
    public void Disable()
    {
        var kb = ResolveInternalKeyboard()
                 ?? throw new InvalidOperationException("Internal keyboard not found.");

        // SAFETY: input blocking is non-persistent (it dies with the app), so a reboot
        // can never leave the keyboard stuck off. Prefer it unless the user opted out.
        bool preferBlock = _config.PreferInputBlock && InputBlockController is not null;

        bool nodeDone = false;
        if (!preferBlock)
        {
            bool disableable = DeviceControl.IsDisableable(kb.InstanceId);
            Logger.Info($"Keyboard '{kb.InstanceId}' disableable={disableable}.");
            if (disableable)
            {
                try
                {
                    DeviceControl.SetEnabled(NativeMethods.GUID_DEVCLASS_KEYBOARD, kb.InstanceId, enable: false);
                    Logger.Info($"Disabled keyboard via device node: {kb.InstanceId}");
                    _method = DisableMethod.DeviceNode;
                    nodeDone = true;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Device-node disable failed: {ex.Message}");
                }
            }
        }

        if (!nodeDone)
        {
            // Input-block path (preferred, or fallback when the node can't be disabled).
            if (InputBlockController is null || (!preferBlock && !_config.AllowInputBlockFallback))
                throw new InvalidOperationException(
                    "Keyboard cannot be disabled at the device level and input-block is unavailable.");

            InputBlockController(true);
            _method = DisableMethod.InputBlock;
            Logger.Info("Keyboard held off via input-block (hardware keys suppressed).");
        }

        // Touchpad is a pointing device: the keyboard hook can't block it, so this only
        // works via device-node disable. Best-effort, non-fatal.
        _touchpadDisabled = false;
        if (_config.DisableTouchpadWithKeyboard)
        {
            try
            {
                var tp = ResolveInternalTouchpad();
                if (tp is not null && DeviceControl.IsDisableable(tp.InstanceId))
                {
                    DeviceControl.SetEnabled(NativeMethods.GUID_DEVCLASS_MOUSE, tp.InstanceId, enable: false);
                    Logger.Info($"Disabled touchpad: {tp.InstanceId}");
                    _touchpadDisabled = true;
                }
                else
                {
                    Logger.Warn("Touchpad not disableable; leaving it enabled.");
                }
            }
            catch (Exception ex) { Logger.Warn($"Touchpad disable failed: {ex.Message}"); }
        }

        KeyboardDisabled = true;
    }

    /// <summary>Re-enable the internal keyboard (and touchpad, if it was disabled).</summary>
    public void Enable()
    {
        switch (_method)
        {
            case DisableMethod.InputBlock:
                InputBlockController?.Invoke(false);
                Logger.Info("Input-block lifted; hardware keys restored.");
                break;

            case DisableMethod.DeviceNode:
                var kb = ResolveInternalKeyboard()
                         ?? throw new InvalidOperationException("Internal keyboard not found.");
                DeviceControl.SetEnabled(NativeMethods.GUID_DEVCLASS_KEYBOARD, kb.InstanceId, enable: true);
                Logger.Info($"Enabled keyboard via device node: {kb.InstanceId}");
                break;
        }

        if (_touchpadDisabled)
        {
            try
            {
                var tp = ResolveInternalTouchpad();
                if (tp is not null)
                {
                    DeviceControl.SetEnabled(NativeMethods.GUID_DEVCLASS_MOUSE, tp.InstanceId, enable: true);
                    Logger.Info($"Enabled touchpad: {tp.InstanceId}");
                }
            }
            catch (Exception ex) { Logger.Warn($"Touchpad enable failed: {ex.Message}"); }
            _touchpadDisabled = false;
        }

        _method = DisableMethod.None;
        KeyboardDisabled = false;
    }

    public void Toggle()
    {
        if (KeyboardDisabled) Enable();
        else Disable();
    }

    /// <summary>
    /// SAFETY: called once at startup. If a previous session (or an older build) left
    /// the internal keyboard disabled at the device level — which persists across
    /// reboots — re-enable it now so the machine always boots with a working keyboard.
    /// Also clears any lingering input block. This is what prevents a lock-out.
    /// </summary>
    public void EnsureInternalKeyboardEnabled()
    {
        InputBlockController?.Invoke(false);
        _method = DisableMethod.None;
        _touchpadDisabled = false;
        KeyboardDisabled = false;

        try
        {
            var kb = ResolveInternalKeyboard();
            if (kb is not null && DeviceControl.IsCurrentlyDisabled(kb.InstanceId))
            {
                DeviceControl.SetEnabled(NativeMethods.GUID_DEVCLASS_KEYBOARD, kb.InstanceId, enable: true);
                Logger.Info($"Startup safety: re-enabled previously-disabled keyboard {kb.InstanceId}.");
            }

            // Same guard for the touchpad, in case a prior session left it disabled.
            var tp = ResolveInternalTouchpad();
            if (tp is not null && DeviceControl.IsCurrentlyDisabled(tp.InstanceId))
            {
                DeviceControl.SetEnabled(NativeMethods.GUID_DEVCLASS_MOUSE, tp.InstanceId, enable: true);
                Logger.Info($"Startup safety: re-enabled previously-disabled touchpad {tp.InstanceId}.");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Startup safety re-enable failed: {ex.Message}");
        }
    }
}

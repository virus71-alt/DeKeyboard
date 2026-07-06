using System.Runtime.InteropServices;
using Dekeyboard.Configuration;
using static Dekeyboard.Interop.NativeMethods;

namespace Dekeyboard.Services;

/// <summary>
/// Global hotkey engine built on a WH_KEYBOARD_LL low-level keyboard hook.
///
/// Why a hook instead of RegisterHotKey? The Windows shell RESERVES Win+&lt;number&gt;
/// (they launch/activate taskbar apps), so RegisterHotKey(MOD_WIN, '5') fails.
/// A low-level hook sees every keystroke first and can both trigger our action and
/// SUPPRESS the keystroke so the taskbar doesn't also react. This makes Win+5 /
/// Win+6 reliable and keeps the hotkeys fully configurable.
///
/// The hook must be installed on a thread that pumps messages - we install it on the
/// WPF UI thread. Actions are dispatched off-thread so device I/O never stalls input.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly AppConfig _config;
    private readonly LowLevelKeyboardProc _proc; // kept alive as a field (no GC)
    private IntPtr _hookHandle = IntPtr.Zero;

    private int _disableVk;
    private int _enableVk;

    public event Action? DisablePressed;
    public event Action? EnablePressed;

    public HotkeyService(AppConfig config)
    {
        _config = config;
        _proc = HookCallback;
        RefreshTargets();
    }

    /// <summary>Recompute target virtual-key codes after a config change.</summary>
    public void RefreshTargets()
    {
        _disableVk = ResolveVk(_config.DisableHotkey.Key);
        _enableVk  = ResolveVk(_config.EnableHotkey.Key);
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero) return;

        // For WH_KEYBOARD_LL the module handle may be the current module; passing the
        // main module handle is what the docs recommend.
        IntPtr hModule = GetModuleHandle(
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName);

        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hModule, 0);
        if (_hookHandle == IntPtr.Zero)
            Logger.Error($"Failed to install keyboard hook. Win32 error {Marshal.GetLastWin32Error()}");
        else
            Logger.Info($"Hotkeys active: disable='{_config.DisableHotkey}', enable='{_config.EnableHotkey}'.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)data.vkCode;

            if (vk == _disableVk && ModifiersMatch(_config.DisableHotkey))
            {
                Fire(DisablePressed);
                if (_config.SuppressHotkeyKeystroke) return (IntPtr)1; // swallow
            }
            else if (vk == _enableVk && ModifiersMatch(_config.EnableHotkey))
            {
                Fire(EnablePressed);
                if (_config.SuppressHotkeyKeystroke) return (IntPtr)1; // swallow
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static void Fire(Action? handler)
    {
        if (handler is null) return;
        // Run device work off the hook thread; returning fast keeps input responsive.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { handler(); }
            catch (Exception ex) { Logger.Error("Hotkey handler failed.", ex); }
        });
    }

    private static bool ModifiersMatch(HotkeyConfig h)
    {
        bool win   = IsDown(VK_LWIN) || IsDown(VK_RWIN);
        bool ctrl  = IsDown(VK_CONTROL);
        bool alt    = IsDown(VK_MENU);
        bool shift = IsDown(VK_SHIFT);

        return win == h.Win && ctrl == h.Ctrl && alt == h.Alt && shift == h.Shift;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Map a config key string ("5", "K", "F9") to a virtual-key code.</summary>
    private static int ResolveVk(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        key = key.Trim();

        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= '0' and <= '9') return c;              // '5' -> 0x35
            if (c is >= 'A' and <= 'Z') return c;              // 'K' -> 0x4B
        }

        // Function keys and named keys via System.Windows.Forms.Keys.
        if (Enum.TryParse<System.Windows.Forms.Keys>(key, true, out var forms))
            return (int)forms;

        Logger.Warn($"Unrecognized hotkey key '{key}'.");
        return 0;
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}

using System.Drawing;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows.Forms;
using Dekeyboard.Configuration;

namespace Dekeyboard.Services;

/// <summary>
/// System-tray presence (Requirement 5), balloon notifications (Requirement 7),
/// and the bonus tray menu: toggle, live status, sound toggle, touchpad toggle,
/// auto-start toggle, view log, quit.
/// </summary>
public sealed class TrayService : IDisposable
{
    // Must match the LogicalName of the embedded icon in the .csproj.
    private const string TrayIconResource = "Dekeyboard.tray.ico";

    private readonly AppConfig _config;
    private readonly DeviceService _device;

    private readonly NotifyIcon _icon;
    private readonly Icon _appIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _autoFoldItem;
    private readonly ToolStripMenuItem _soundItem;
    private readonly ToolStripMenuItem _touchpadItem;
    private readonly ToolStripMenuItem _startupItem;

    public event Action? QuitRequested;

    /// <summary>Raised when the user toggles "Auto-lock in tablet mode" (true = on).</summary>
    public event Action<bool>? AutoFoldChanged;

    public TrayService(AppConfig config, DeviceService device)
    {
        _config = config;
        _device = device;
        _appIcon = LoadAppIcon();

        _statusItem = new ToolStripMenuItem("Status: keyboard enabled") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Disable laptop keyboard", null, (_, _) => ToggleFromMenu());
        _autoFoldItem = new ToolStripMenuItem("Auto-lock in tablet mode", null, (_, _) => ToggleAutoFold())
            { Checked = _config.AutoFoldDetection, CheckOnClick = true };
        _soundItem = new ToolStripMenuItem("Play sound on toggle", null, (_, _) => ToggleSound())
            { Checked = _config.PlaySound, CheckOnClick = true };
        _touchpadItem = new ToolStripMenuItem("Also disable touchpad", null, (_, _) => ToggleTouchpad())
            { Checked = _config.DisableTouchpadWithKeyboard, CheckOnClick = true };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
            { CheckOnClick = true };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoFoldItem);
        menu.Items.Add(_soundItem);
        menu.Items.Add(_touchpadItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("View log...", null, (_, _) => Logger.OpenInEditor()));
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => QuitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = _appIcon,
            Visible = true,
            Text = "Dekeyboard",
            ContextMenuStrip = menu
        };
        // Double-click the tray icon = toggle (bonus feature).
        _icon.DoubleClick += (_, _) => ToggleFromMenu();

        // Reflect the real auto-start state at launch.
        _startupItem.Checked = StartupService.IsEnabled();

        UpdateStatusUi();
    }

    /// <summary>Load the app icon embedded in the assembly; fall back to a system icon.</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(TrayIconResource);
            if (s is not null) return new Icon(s);
            Logger.Warn($"Embedded icon '{TrayIconResource}' not found; using system icon.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load embedded app icon.", ex);
        }
        return SystemIcons.Application;
    }

    private void ToggleFromMenu()
    {
        try
        {
            bool wasDisabled = _device.KeyboardDisabled;
            _device.Toggle();
            OnStateChanged(nowDisabled: !wasDisabled);
        }
        catch (Exception ex)
        {
            Logger.Error("Toggle from tray failed.", ex);
            Notify("Dekeyboard - error", ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>Called by the hotkey handlers after a successful state change.</summary>
    public void OnStateChanged(bool nowDisabled)
    {
        UpdateStatusUi();

        if (_config.PlaySound)
            (nowDisabled ? SystemSounds.Hand : SystemSounds.Asterisk).Play();

        string detail = nowDisabled && _device.CurrentMethod == DeviceService.DisableMethod.InputBlock
            ? " (input-block mode)"
            : string.Empty;

        Notify(
            title: "Dekeyboard",
            message: (nowDisabled ? "Laptop keyboard disabled" : "Laptop keyboard enabled") + detail,
            icon: ToolTipIcon.Info);
    }

    public void NotifyError(string message)
        => Notify("Dekeyboard - error", message, ToolTipIcon.Error);

    private void UpdateStatusUi()
    {
        // NotifyIcon lives on the UI thread; marshal if needed.
        if (_statusItem.GetCurrentParent()?.InvokeRequired == true)
        {
            _statusItem.GetCurrentParent()!.BeginInvoke(new Action(UpdateStatusUi));
            return;
        }

        bool disabled = _device.KeyboardDisabled;
        _statusItem.Text = disabled ? "Status: keyboard DISABLED" : "Status: keyboard enabled";
        _toggleItem.Text = disabled ? "Enable laptop keyboard" : "Disable laptop keyboard";
        _icon.Text = disabled ? "Dekeyboard - disabled" : "Dekeyboard - enabled";
    }

    private void Notify(string title, string message, ToolTipIcon icon)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(2500);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to show notification.", ex);
        }
    }

    private void ToggleAutoFold()
    {
        _config.AutoFoldDetection = _autoFoldItem.Checked;
        _config.Save();
        AutoFoldChanged?.Invoke(_autoFoldItem.Checked);
    }

    private void ToggleSound()
    {
        _config.PlaySound = _soundItem.Checked;
        _config.Save();
    }

    private void ToggleTouchpad()
    {
        _config.DisableTouchpadWithKeyboard = _touchpadItem.Checked;
        _config.Save();
    }

    private void ToggleStartup()
    {
        StartupService.SetEnabled(_startupItem.Checked);
        // Re-sync in case the operation failed.
        _startupItem.Checked = StartupService.IsEnabled();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon.Dispose();
    }
}

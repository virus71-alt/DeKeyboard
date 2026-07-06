using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using Dekeyboard.Configuration;
using Dekeyboard.Services;

namespace Dekeyboard;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private AppConfig _config = null!;
    private DeviceService _device = null!;
    private HotkeyService _hotkeys = null!;
    private TrayService _tray = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --- Single instance -------------------------------------------------------
        _singleInstance = new Mutex(true, "Dekeyboard_SingleInstance_Mutex", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        // --- Global exception safety nets (Requirement 12) -------------------------
        DispatcherUnhandledException += (_, ex) =>
        {
            Logger.Error("Unhandled UI exception.", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Logger.Error("Unhandled domain exception.", ex.ExceptionObject as Exception);

        Logger.Info("=== Dekeyboard starting ===");

        // --- Ensure elevation (Requirement 10) ------------------------------------
        // The manifest already requests requireAdministrator, but if the app was
        // rebuilt as asInvoker we self-elevate by relaunching with the runas verb.
        if (!IsElevated())
        {
            if (TryRelaunchElevated())
            {
                Shutdown();
                return;
            }
            // Fully qualified: System.Windows.Forms is globally imported (UseWindowsForms),
            // so the bare name MessageBox would be ambiguous with the WinForms one.
            System.Windows.MessageBox.Show(
                "Dekeyboard needs administrator rights to enable/disable devices.",
                "Dekeyboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // --- Compose services ------------------------------------------------------
        _config = AppConfig.Load();
        _device = new DeviceService(_config);
        _tray = new TrayService(_config, _device);
        _hotkeys = new HotkeyService(_config);

        _tray.QuitRequested += () => Shutdown();

        _hotkeys.DisablePressed += HandleDisable;
        _hotkeys.EnablePressed += HandleEnable;
        _hotkeys.Install();

        // Log the identified device up front so the user can verify detection.
        try { _device.ResolveInternalKeyboard(); }
        catch (Exception ex) { Logger.Error("Initial keyboard detection failed.", ex); }

        Logger.Info("Dekeyboard ready (running in tray).");
    }

    private void HandleDisable()
    {
        try
        {
            if (_device.KeyboardDisabled) return; // already disabled
            _device.Disable();
            Dispatcher.Invoke(() => _tray.OnStateChanged(nowDisabled: true));
        }
        catch (Exception ex)
        {
            Logger.Error("Disable failed.", ex);
            Dispatcher.Invoke(() => _tray.NotifyError("Could not disable keyboard: " + ex.Message));
        }
    }

    private void HandleEnable()
    {
        try
        {
            if (!_device.KeyboardDisabled) return; // already enabled
            _device.Enable();
            Dispatcher.Invoke(() => _tray.OnStateChanged(nowDisabled: false));
        }
        catch (Exception ex)
        {
            Logger.Error("Enable failed.", ex);
            Dispatcher.Invoke(() => _tray.NotifyError("Could not enable keyboard: " + ex.Message));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Safety: never leave the keyboard disabled after we quit.
        try
        {
            if (_device is { KeyboardDisabled: true })
            {
                _device.Enable();
                Logger.Info("Re-enabled keyboard on exit.");
            }
        }
        catch (Exception ex) { Logger.Error("Failed to re-enable keyboard on exit.", ex); }

        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        Logger.Info("=== Dekeyboard stopped ===");
        base.OnExit(e);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryRelaunchElevated()
    {
        try
        {
            string exe = Process.GetCurrentProcess().MainModule!.FileName;
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas" // triggers the UAC prompt
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Self-elevation failed (user may have declined UAC).", ex);
            return false;
        }
    }
}

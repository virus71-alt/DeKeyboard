using Windows.Devices.Sensors;
using static Dekeyboard.Interop.NativeMethods;

namespace Dekeyboard.Services;

/// <summary>
/// Detects tablet vs. laptop posture and raises lock/unlock events.
///
/// Primary signal: the physical device pose from the accelerometer
/// (<see cref="SimpleOrientationSensor"/>). Laptop mode reads <c>NotRotated</c>;
/// folding to tablet gives <c>Faceup</c> (flat) or one of the rotated poses. This is
/// STABLE while folded, unlike the display orientation, which snaps back to landscape
/// once Windows finishes auto-rotating (that caused the "locks then instantly unlocks"
/// bug).
///
/// Fallback (older machines / no sensor projection): display orientation via
/// EnumDisplaySettings.
///
/// A debounce means a posture must persist briefly before we act, so a momentary
/// rotation during the fold motion can't toggle the keyboard.
/// </summary>
public sealed class FoldDetectionService : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(1200);

    private readonly object _gate = new();
    private SimpleOrientationSensor? _sensor;
    private System.Threading.Timer? _pollTimer;     // display-orientation fallback
    private System.Threading.Timer? _debounceTimer;
    private bool _running;

    private bool _committedTablet;   // last state we actually acted on
    private bool _pendingTablet;

    public event Action? FoldedToTablet;
    public event Action? UnfoldedToLaptop;

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _debounceTimer = new System.Threading.Timer(DebounceFire, null, Timeout.Infinite, Timeout.Infinite);

            try { _sensor = SimpleOrientationSensor.GetDefault(); }
            catch (Exception ex) { Logger.Warn($"Orientation sensor unavailable: {ex.Message}"); }

            if (_sensor is not null)
            {
                _committedTablet = _pendingTablet = IsTablet(_sensor.GetCurrentOrientation());
                _sensor.OrientationChanged += OnSensorChanged;
                Logger.Info($"Fold detection started via accelerometer (SimpleOrientationSensor). " +
                            $"Initial={(_committedTablet ? "TABLET" : "laptop")}. Debounce {Debounce.TotalSeconds:0.#}s.");
            }
            else
            {
                _committedTablet = _pendingTablet = DisplayIsTablet(out int o);
                _pollTimer = new System.Threading.Timer(PollDisplay, null, 1000, 1000);
                Logger.Info($"Fold detection started via display-orientation fallback " +
                            $"(no accelerometer). Initial dmDisplayOrientation={o}.");
            }
            _running = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            if (_sensor is not null) _sensor.OrientationChanged -= OnSensorChanged;
            _sensor = null;
            _pollTimer?.Dispose(); _pollTimer = null;
            _debounceTimer?.Dispose(); _debounceTimer = null;
            _running = false;
            Logger.Info("Fold detection stopped.");
        }
    }

    private static bool IsTablet(SimpleOrientation o) => o != SimpleOrientation.NotRotated;

    private void OnSensorChanged(SimpleOrientationSensor sender, SimpleOrientationSensorOrientationChangedEventArgs e)
        => Propose(IsTablet(e.Orientation), $"accelerometer={e.Orientation}");

    private void PollDisplay(object? _)
    {
        bool tablet = DisplayIsTablet(out int o);
        Propose(tablet, $"dmDisplayOrientation={o}");
    }

    private static bool DisplayIsTablet(out int orientation)
    {
        orientation = 0;
        var dm = new DEVMODE { dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            orientation = (int)dm.dmDisplayOrientation;
        return orientation != 0;
    }

    /// <summary>Record a candidate posture and (re)arm the debounce timer.</summary>
    private void Propose(bool tablet, string detail)
    {
        lock (_gate)
        {
            if (!_running) return;
            _pendingTablet = tablet;
            _lastDetail = detail;
            _debounceTimer?.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private string _lastDetail = "";

    private void DebounceFire(object? _)
    {
        Action? fire = null;
        lock (_gate)
        {
            if (!_running || _pendingTablet == _committedTablet) return;
            _committedTablet = _pendingTablet;
            Logger.Info($"Posture settled -> {(_committedTablet ? "TABLET" : "laptop")} ({_lastDetail}).");
            fire = _committedTablet ? FoldedToTablet : UnfoldedToLaptop;
        }
        try { fire?.Invoke(); }
        catch (Exception ex) { Logger.Error("Fold-detection handler failed.", ex); }
    }

    public void Dispose() => Stop();
}

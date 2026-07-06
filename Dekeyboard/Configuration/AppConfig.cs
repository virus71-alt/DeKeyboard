using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dekeyboard.Configuration;

/// <summary>A single hotkey combination.</summary>
public sealed class HotkeyConfig
{
    public bool Win   { get; set; }
    public bool Ctrl  { get; set; }
    public bool Alt   { get; set; }
    public bool Shift { get; set; }

    /// <summary>Main key, e.g. "5", "6", "K", "F9".</summary>
    public string Key { get; set; } = "";

    public override string ToString()
    {
        var parts = new List<string>();
        if (Win) parts.Add("Win");
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}

/// <summary>Persisted user configuration. Lives in %APPDATA%\Dekeyboard\config.json.</summary>
public sealed class AppConfig
{
    // Requirement 1 & 2: Win+5 disables, Win+6 enables (configurable).
    public HotkeyConfig DisableHotkey { get; set; } = new() { Win = true, Key = "5" };
    public HotkeyConfig EnableHotkey  { get; set; } = new() { Win = true, Key = "6" };

    /// <summary>Play a sound when the state changes (bonus feature).</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>Disable the internal touchpad together with the keyboard (bonus feature).</summary>
    public bool DisableTouchpadWithKeyboard { get; set; } = false;

    /// <summary>
    /// When the internal keyboard cannot be disabled at the device level (common on
    /// convertibles where "Disable device" is greyed out), fall back to blocking
    /// hardware keystrokes with a low-level hook. Note: this fallback blocks ALL
    /// physical keyboards (it can't isolate one device at the input layer); on-screen
    /// and touch keyboards keep working.
    /// </summary>
    public bool AllowInputBlockFallback { get; set; } = true;

    /// <summary>
    /// SAFETY DEFAULT. Use the non-persistent input-block method instead of disabling
    /// the device node. A device-node disable survives a reboot and can leave you
    /// locked out; input blocking dies with the app, so a restart always restores the
    /// keyboard. Keep this true unless you specifically want a hard device disable.
    /// </summary>
    public bool PreferInputBlock { get; set; } = true;

    /// <summary>
    /// Automatically lock the keyboard when the device is folded into tablet (360°)
    /// mode and unlock it when returned to laptop mode. Uses SM_CONVERTIBLESLATEMODE.
    /// </summary>
    public bool AutoFoldDetection { get; set; } = true;

    /// <summary>
    /// Swallow the key event once handled so Win+5/Win+6 don't also trigger the
    /// shell's "launch 5th taskbar app" behavior.
    /// </summary>
    public bool SuppressHotkeyKeystroke { get; set; } = true;

    /// <summary>
    /// Optional explicit device-instance-id overrides. When null the app auto-detects.
    /// Set these if auto-detection picks the wrong device (see the log for candidates).
    /// </summary>
    public string? KeyboardInstanceId { get; set; }
    public string? TouchpadInstanceId { get; set; }

    // ---- persistence --------------------------------------------------------------

    [JsonIgnore]
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Dekeyboard");

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (cfg is not null) return cfg;
            }
        }
        catch (Exception ex)
        {
            Services.Logger.Error("Failed to load config, using defaults.", ex);
        }

        var fresh = new AppConfig();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Services.Logger.Error("Failed to save config.", ex);
        }
    }
}

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

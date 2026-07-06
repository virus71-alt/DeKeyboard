using System.Diagnostics;

namespace Dekeyboard.Services;

/// <summary>
/// Manages "start with Windows" (Requirement 6).
///
/// Because the app requires administrator rights, a plain HKCU\...\Run entry would
/// pop a UAC prompt at every logon. Instead we register a Scheduled Task set to
/// "Run with highest privileges", which starts the app elevated at logon WITHOUT a
/// UAC prompt. We shell out to the built-in schtasks.exe so there is no extra
/// dependency.
/// </summary>
public static class StartupService
{
    private const string TaskName = "Dekeyboard_AutoStart";

    // Environment.ProcessPath is correct even for single-file published apps
    // (Assembly.Location returns "" there and triggers IL3000).
    private static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? AppContext.BaseDirectory;

    public static bool IsEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not query auto-start task.", ex);
            return false;
        }
    }

    public static void Enable()
    {
        // /RL HIGHEST => elevated, /SC ONLOGON => at user logon, /F => overwrite.
        string args =
            $"/Create /TN \"{TaskName}\" /TR \"\\\"{ExePath}\\\"\" /SC ONLOGON /RL HIGHEST /F";
        Run(args, "enable auto-start");
    }

    public static void Disable()
    {
        Run($"/Delete /TN \"{TaskName}\" /F", "disable auto-start");
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled) Enable(); else Disable();
    }

    private static void Run(string args, string what)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            if (p.ExitCode != 0)
                Logger.Error($"schtasks failed to {what} (exit {p.ExitCode}): {err}");
            else
                Logger.Info($"Auto-start: {what} succeeded.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to {what}.", ex);
        }
    }
}

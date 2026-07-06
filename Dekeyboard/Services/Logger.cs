using System.IO;

namespace Dekeyboard.Services;

/// <summary>
/// Minimal thread-safe file logger. Writes to %APPDATA%\Dekeyboard\log.txt.
/// Requirement 12: handle errors gracefully and log them.
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();

    private static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Dekeyboard");

    private static string LogPath => Path.Combine(LogDirectory, "log.txt");

    public static void Info(string message)  => Write("INFO ", message, null);
    public static void Warn(string message)  => Write("WARN ", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);

                // Basic size cap: keep the log from growing unbounded.
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 1_000_000)
                    File.WriteAllText(LogPath, ""); // truncate

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null)
                    line += Environment.NewLine + ex;

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never throw and take the app down.
        }
    }

    public static void OpenInEditor()
    {
        try
        {
            if (!File.Exists(LogPath)) Info("Log opened.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LogPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Error("Could not open log file.", ex);
        }
    }
}

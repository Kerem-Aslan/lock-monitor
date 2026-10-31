using System.Diagnostics;
using System.Text;

namespace LockMonitor;

/// <summary>
/// Tiny append-only log for lifecycle events (create/activate/deactivate/delete).
/// Writes only on rare events, never per sample, and caps the file at 512 KB.
/// In a packaged app %LOCALAPPDATA% is redirected into the package's LocalCache folder.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly Lock Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LockMonitor", "provider.log");

    public static string Location => FilePath;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
        Debug.Write(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the provider down.
        }
    }
}

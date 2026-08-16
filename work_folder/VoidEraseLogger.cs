using System.Text;

namespace VoidErase;

internal static class VoidEraseLogger
{
    private static readonly object Sync = new();

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidErase", "Logs");

    public static void Write(string message)
    {
        if (!VoidEraseSettings.KeepLogs) return;

        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"VoidErase-{DateTime.Now:yyyy-MM-dd}.log");
            lock (Sync)
            {
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void Error(string message, Exception? ex = null) =>
        Write($"ERROR: {message}{(ex == null ? "" : " | " + ex.GetType().Name + ": " + ex.Message)}");
}

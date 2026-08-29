using System;
using System.IO;
using System.Text;

namespace VoidErase;

internal static class VoidEraseLogger
{
    private static readonly object Sync = new();
    private static readonly string SessionId = Guid.NewGuid().ToString("N");
    private const long MaxLogBytes = 5 * 1024 * 1024;

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidErase", "Logs");

    public static void Write(string message)
    {
        if (!VoidEraseSettings.KeepLogs) return;

        try
        {
            Directory.CreateDirectory(LogDirectory);
            string path = Path.Combine(LogDirectory, $"VoidErase-{DateTime.UtcNow:yyyy-MM-dd}.log");
            lock (Sync)
            {
                RotateIfNeeded(path);
                File.AppendAllText(
                    path,
                    $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} [{SessionId}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Günlükleme, ana silme işlemini hiçbir koşulda durdurmamalıdır.
        }
    }

    public static void Error(string message, Exception? ex = null) =>
        Write($"ERROR: {message}{(ex == null ? "" : " | " + ex.GetType().Name + ": " + ex.Message)}");

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes) return;

        string rotated = path + ".1";
        try
        {
            if (File.Exists(rotated)) File.Delete(rotated);
            File.Move(path, rotated);
        }
        catch
        {
            // Kilitli veya erişilemeyen günlükte yazma denemesi yine devam eder.
        }
    }
}

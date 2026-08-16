using System;
using System.Drawing;
using System.Windows.Forms;

namespace VoidErase;

internal sealed class OperationSummaryForm : Form
{
    public OperationSummaryForm(OperationResult result, bool english)
    {
        Text = english ? "Operation completed" : "İşlem tamamlandı";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 380);
        BackColor = Color.FromArgb(244, 247, 250);

        var title = new Label
        {
            Text = english ? "Operation completed" : "İşlem tamamlandı",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 42, 61),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        Controls.Add(title);

        string targetName = string.IsNullOrWhiteSpace(result.TargetPath)
            ? "-"
            : System.IO.Path.GetFileName(
                result.TargetPath.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(targetName))
            targetName = result.TargetPath;

        string started = result.StartedAt == default
            ? "-"
            : result.StartedAt.ToString("yyyy-MM-dd HH:mm:ss");

        string elapsed = result.Elapsed == default
            ? "-"
            : FormatElapsed(result.Elapsed);

        var body = new Label
        {
            Text = english
                ? $"Target: {targetName}\r\n" +
                  $"Started: {started}\r\n" +
                  $"Duration: {elapsed}\r\n\r\n" +
                  $"Files: {result.TotalFiles:N0}\r\n" +
                  $"Total size: {FormatSize(result.TotalBytes)}\r\n" +
                  $"Successful: {result.Successful:N0}\r\n" +
                  $"Failed: {result.Failed:N0}\r\n" +
                  $"Verified: {result.Verified:N0}\r\n" +
                  $"Cancelled: {(result.Cancelled ? "Yes" : "No")}\r\n\r\n" +
                  "Method: AES-256-GCM + SHA-256 verification"
                : $"Hedef: {targetName}\r\n" +
                  $"Başlangıç: {started}\r\n" +
                  $"Süre: {elapsed}\r\n\r\n" +
                  $"Dosyalar: {result.TotalFiles:N0}\r\n" +
                  $"Toplam boyut: {FormatSize(result.TotalBytes)}\r\n" +
                  $"Başarılı: {result.Successful:N0}\r\n" +
                  $"Başarısız: {result.Failed:N0}\r\n" +
                  $"Doğrulanan: {result.Verified:N0}\r\n" +
                  $"İptal edildi: {(result.Cancelled ? "Evet" : "Hayır")}\r\n\r\n" +
                  "Yöntem: AES-256-GCM + SHA-256 doğrulama",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(55, 69, 82),
            AutoSize = true,
            Location = new Point(30, 72)
        };
        Controls.Add(body);

        var close = new Button
        {
            Text = english ? "Close" : "Kapat",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.System,
            Size = new Size(100, 34),
            Location = new Point(370, 325)
        };

        Controls.Add(close);
        AcceptButton = close;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0)
            return "-";

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double n = bytes;
        int i = 0;

        while (n >= 1024 && i < units.Length - 1)
        {
            n /= 1024;
            i++;
        }

        return $"{n:0.##} {units[i]}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

        return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
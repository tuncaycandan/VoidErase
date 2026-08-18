using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VoidErase;

internal sealed class OperationSummaryForm : Form
{
    public OperationSummaryForm(OperationResult result, bool english)
    {
        Text = english ? "Operation summary" : "İşlem özeti";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 620);
        BackColor = Color.FromArgb(244, 247, 250);

        var title = new Label
        {
            Text = english ? "Operation summary" : "İşlem özeti",
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

        string media = FormatMedia(result.MediaKind, english);
        string assurance = FormatAssurance(result.SanitizationAssurance, english);
        string method = FormatMethod(result.SanitizationMethod, english);

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
                  $"Skipped: {result.Skipped:N0}\r\n" +
                  $"Verified: {result.Verified:N0}\r\n" +
                  $"Cancelled: {(result.Cancelled ? "Yes" : "No")}\r\n\r\n" +
                  $"Media: {media}\r\n" +
                  $"Sanitization method: {method}\r\n" +
                  $"Assurance: {assurance}\r\n" +
                  "File operation: AES-256-GCM + SHA-256 verification"
                : $"Hedef: {targetName}\r\n" +
                  $"Başlangıç: {started}\r\n" +
                  $"Süre: {elapsed}\r\n\r\n" +
                  $"Dosyalar: {result.TotalFiles:N0}\r\n" +
                  $"Toplam boyut: {FormatSize(result.TotalBytes)}\r\n" +
                  $"Başarılı: {result.Successful:N0}\r\n" +
                  $"Başarısız: {result.Failed:N0}\r\n" +
                  $"Atlanan: {result.Skipped:N0}\r\n" +
                  $"Doğrulanan: {result.Verified:N0}\r\n" +
                  $"İptal edildi: {(result.Cancelled ? "Evet" : "Hayır")}\r\n\r\n" +
                  $"Medya: {media}\r\n" +
                  $"Sanitizasyon yöntemi: {method}\r\n" +
                  $"Güvence: {assurance}\r\n" +
                  "Dosya işlemi: AES-256-GCM + SHA-256 doğrulama",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(55, 69, 82),
            AutoSize = true,
            Location = new Point(30, 72)
        };
        Controls.Add(body);

        string recommendation = english
            ? "Recommendation: " + result.SanitizationRecommendation
            : "Öneri: " + result.SanitizationRecommendation;

        var recommendationLabel = new Label
        {
            Text = recommendation,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(101, 115, 130),
            AutoSize = false,
            Location = new Point(30, 360),
            Size = new Size(560, 62)
        };
        Controls.Add(recommendationLabel);

        var listLabel = new Label
        {
            Text = english ? "Details" : "Ayrıntılar",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 42, 61),
            AutoSize = true,
            Location = new Point(30, 430)
        };
        Controls.Add(listLabel);

        var list = new ListBox
        {
            Location = new Point(30, 455),
            Size = new Size(560, 105),
            HorizontalScrollbar = true,
            Font = new Font("Segoe UI", 9)
        };

        AddItems(list, english ? "SUCCESS" : "BAŞARILI", result.SuccessfulFiles);
        AddItems(list, english ? "FAILED" : "BAŞARISIZ", result.FailedFiles);
        AddItems(list, english ? "SKIPPED" : "ATLANDI", result.SkippedFiles);

        Controls.Add(list);

        var close = new Button
        {
            Text = english ? "Close" : "Kapat",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.System,
            Size = new Size(100, 34),
            Location = new Point(490, 570)
        };

        Controls.Add(close);
        AcceptButton = close;
    }

    private static string FormatMedia(MediaKind media, bool english)
    {
        return media switch
        {
            MediaKind.Magnetic => english ? "Magnetic / HDD" : "Manyetik / HDD",
            MediaKind.SolidState => english ? "Solid-state / SSD" : "Katı hal / SSD",
            MediaKind.Removable => english ? "Removable" : "Çıkarılabilir",
            MediaKind.Virtual => english ? "Virtual / network" : "Sanal / ağ",
            MediaKind.Optical => english ? "Optical" : "Optik",
            _ => english ? "Unknown" : "Bilinmiyor"
        };
    }

    private static string FormatAssurance(SanitizationAssurance assurance, bool english)
    {
        return assurance switch
        {
            SanitizationAssurance.ApplicationLevelOnly => english
                ? "Application-level only; media sanitization not established"
                : "Yalnızca uygulama düzeyi; medya sanitizasyonu doğrulanmadı",
            SanitizationAssurance.MediaLevelCandidate => english
                ? "Media-level candidate"
                : "Medya düzeyi adayı",
            SanitizationAssurance.MediaLevelVerified => english
                ? "Media-level verified"
                : "Medya düzeyi doğrulandı",
            _ => english ? "Not established" : "Belirlenmedi"
        };
    }

    private static string FormatMethod(SanitizationMethod method, bool english)
    {
        return method switch
        {
            SanitizationMethod.Clear => "Clear",
            SanitizationMethod.Purge => "Purge",
            SanitizationMethod.CryptographicErase => english ? "Cryptographic Erase" : "Kriptografik Silme",
            SanitizationMethod.Destroy => english ? "Destroy" : "Fiziksel Yok Etme",
            _ => english ? "None / application-level file operation" : "Yok / uygulama düzeyi dosya işlemi"
        };
    }

    private static void AddItems(
        ListBox list,
        string category,
        System.Collections.Generic.IEnumerable<string> files)
    {
        foreach (string file in files)
            list.Items.Add($"{category}: {file}");
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
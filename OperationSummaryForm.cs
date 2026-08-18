using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VoidErase;

internal sealed class OperationSummaryForm : Form
{
    private static readonly Color BackgroundColor =
        Color.FromArgb(244, 247, 250);

    private static readonly Color CardColor =
        Color.White;

    private static readonly Color BorderColor =
        Color.FromArgb(214, 222, 231);

    private static readonly Color TextPrimary =
        Color.FromArgb(31, 42, 52);

    private static readonly Color TextSecondary =
        Color.FromArgb(101, 115, 130);

    private static readonly Color SuccessColor =
        Color.FromArgb(30, 145, 88);

    private static readonly Color FailedColor =
        Color.FromArgb(211, 63, 63);

    private static readonly Color WarningColor =
        Color.FromArgb(190, 130, 35);

    private static readonly Color AccentColor =
        Color.FromArgb(25, 150, 220);

    public OperationSummaryForm(OperationResult result, bool english)
    {
        Text = english ? "Operation Summary" : "İşlem Özeti";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(680, 615);
        BackColor = BackgroundColor;
        Font = new Font("Segoe UI", 9F);

        bool success =
            !result.Cancelled &&
            result.Failed == 0 &&
            result.Skipped == 0;

        bool cancelled = result.Cancelled;

        Color statusColor =
            cancelled
                ? FailedColor
                : success
                    ? SuccessColor
                    : WarningColor;

        string statusTitle =
            cancelled
                ? english ? "Operation cancelled" : "İşlem iptal edildi"
                : success
                    ? english ? "Erasure completed" : "Silme tamamlandı"
                    : english ? "Operation completed with warnings" : "İşlem uyarılarla tamamlandı";

        string statusDetail =
            cancelled
                ? english
                    ? "Completed files were processed; remaining files were preserved."
                    : "Tamamlanan dosyalar işlendi; kalan dosyalar korundu."
                : success
                    ? english
                        ? "All selected files were successfully erased and verified."
                        : "Seçilen tüm dosyalar başarıyla yok edildi ve doğrulandı."
                    : english
                        ? "Some files could not be processed."
                        : "Bazı dosyalar işlenemedi.";

        Panel header = new()
        {
            Location = new Point(20, 18),
            Size = new Size(640, 88),
            BackColor = CardColor,
            BorderStyle = BorderStyle.FixedSingle
        };

        Panel statusBar = new()
        {
            Location = new Point(0, 0),
            Size = new Size(6, 86),
            BackColor = statusColor
        };

        Label status = new()
        {
            Text = statusTitle,
            Location = new Point(24, 14),
            Size = new Size(580, 28),
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = statusColor
        };

        Label detail = new()
        {
            Text = statusDetail,
            Location = new Point(24, 45),
            Size = new Size(580, 30),
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextSecondary
        };

        header.Controls.AddRange(new Control[]
        {
            statusBar,
            status,
            detail
        });

        Controls.Add(header);

        string targetName = GetTargetName(result.TargetPath);

        Label targetHeading = CreateSectionLabel(
            english ? "TARGET" : "HEDEF");

        targetHeading.SetBounds(20, 120, 150, 20);
        Controls.Add(targetHeading);

        Panel targetCard = CreateCard(20, 144, 640, 58);

        Label targetLabel = new()
        {
            Text = targetName,
            Location = new Point(16, 9),
            Size = new Size(600, 22),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoEllipsis = true
        };

        string started = result.StartedAt == default
            ? "-"
            : result.StartedAt.ToString("yyyy-MM-dd HH:mm:ss");

        Label targetInfo = new()
        {
            Text = english
                ? $"Started: {started}"
                : $"Başlangıç: {started}",
            Location = new Point(16, 31),
            Size = new Size(600, 18),
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = TextSecondary
        };

        targetCard.Controls.AddRange(new Control[]
        {
            targetLabel,
            targetInfo
        });

        Controls.Add(targetCard);

        Label statsHeading = CreateSectionLabel(
            english ? "RESULTS" : "SONUÇLAR");

        statsHeading.SetBounds(20, 216, 150, 20);
        Controls.Add(statsHeading);

        Panel filesCard = CreateStatCard(
            20,
            240,
            148,
            82,
            english ? "FILES" : "DOSYA",
            result.TotalFiles.ToString("N0"),
            AccentColor);

        Panel sizeCard = CreateStatCard(
            178,
            240,
            148,
            82,
            english ? "TOTAL SIZE" : "TOPLAM BOYUT",
            FormatSize(result.TotalBytes),
            TextPrimary);

        Panel successCard = CreateStatCard(
            336,
            240,
            148,
            82,
            english ? "SUCCESSFUL" : "BAŞARILI",
            result.Successful.ToString("N0"),
            SuccessColor);

        Panel verifiedCard = CreateStatCard(
            494,
            240,
            166,
            82,
            english ? "VERIFIED" : "DOĞRULANAN",
            result.Verified.ToString("N0"),
            SuccessColor);

        Controls.AddRange(new Control[]
        {
            filesCard,
            sizeCard,
            successCard,
            verifiedCard
        });

        Panel warningCard = CreateCard(20, 334, 640, 42);

        Label warningText = new()
        {
            Text = english
                ? $"Failed: {result.Failed:N0}    •    Skipped: {result.Skipped:N0}    •    Duration: {FormatElapsed(result.Elapsed)}"
                : $"Başarısız: {result.Failed:N0}    •    Atlandı: {result.Skipped:N0}    •    Süre: {FormatElapsed(result.Elapsed)}",
            Location = new Point(16, 12),
            Size = new Size(608, 22),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor =
                result.Failed > 0
                    ? FailedColor
                    : result.Skipped > 0
                        ? WarningColor
                        : TextSecondary
        };

        warningCard.Controls.Add(warningText);
        Controls.Add(warningCard);
		Label metadataHeading = CreateSectionLabel(
    english ? "SANITIZATION METADATA" : "SANİTİZASYON BİLGİLERİ");

metadataHeading.SetBounds(20, 380, 260, 20);
Controls.Add(metadataHeading);

Panel metadataCard = CreateCard(20, 400, 640, 70);

Label metadata = new()
{
    Text = english
        ? "Method: Cryptographic transformation + verified deletion    •    " +
          "Scope: Application-level file sanitization    •    " +
          "Verification: AES-256-GCM + SHA-256    •    " +
          "NIST SP 800-88 Rev. 2: terminology-aligned reporting only; " +
          "media-level sanitization is not claimed."
        : "Yöntem: Kriptografik dönüştürme + doğrulanmış silme    •    " +
          "Kapsam: Uygulama düzeyi dosya sanitizasyonu    •    " +
          "Doğrulama: AES-256-GCM + SHA-256    •    " +
          "NIST SP 800-88 Rev. 2: yalnızca terminoloji uyumlu raporlama; " +
          "medya düzeyi sanitizasyon iddia edilmez.",

    Location = new Point(16, 12),
    Size = new Size(608, 46),
    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    ForeColor = TextSecondary,
    AutoEllipsis = true
};

metadataCard.Controls.Add(metadata);
Controls.Add(metadataCard);

        Label detailsHeading = CreateSectionLabel(
            english ? "DETAILS" : "AYRINTILAR");

        detailsHeading.SetBounds(20, 473, 150, 20);
        Controls.Add(detailsHeading);

        ListBox list = new()
        {
            Location = new Point(20, 495),
            Size = new Size(500, 70),
            Font = new Font("Segoe UI", 8.5F),
            BorderStyle = BorderStyle.FixedSingle,
            HorizontalScrollbar = true,
            BackColor = Color.White,
            ForeColor = TextPrimary
        };

        AddItems(
            list,
            english ? "SUCCESS" : "BAŞARILI",
            result.SuccessfulFiles);

        AddItems(
            list,
            english ? "FAILED" : "BAŞARISIZ",
            result.FailedFiles);

        AddItems(
            list,
            english ? "SKIPPED" : "ATLANDI",
            result.SkippedFiles);

        Controls.Add(list);

        Panel methodCard = CreateCard(532, 495, 128, 70);

        Label method = new()
        {
            Text = english
                ? "AES-256-GCM\nSHA-256 verification"
                : "AES-256-GCM\nSHA-256 doğrulama",
            Location = new Point(10, 12),
            Size = new Size(108, 46),
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter
        };

        methodCard.Controls.Add(method);
        Controls.Add(methodCard);

        Button close = new()
        {
            Text = english ? "Close" : "Kapat",
            DialogResult = DialogResult.OK,
            Size = new Size(110, 34),
            Location = new Point(550, 570),
            FlatStyle = FlatStyle.Flat,
            BackColor = CardColor,
            ForeColor = TextPrimary,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };

        close.FlatAppearance.BorderSize = 1;
        close.FlatAppearance.BorderColor = BorderColor;
        close.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(242, 245, 248);

        Controls.Add(close);

        AcceptButton = close;
        CancelButton = close;
    }

    private static Panel CreateCard(
        int x,
        int y,
        int width,
        int height)
    {
        return new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = CardColor,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = AccentColor
        };
    }

    private static Panel CreateStatCard(
        int x,
        int y,
        int width,
        int height,
        string caption,
        string value,
        Color valueColor)
    {
        Panel card = CreateCard(x, y, width, height);

        Label captionLabel = new()
        {
            Text = caption,
            Location = new Point(12, 9),
            Size = new Size(width - 24, 18),
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = TextSecondary
        };

        Label valueLabel = new()
        {
            Text = value,
            Location = new Point(12, 30),
            Size = new Size(width - 24, 36),
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = valueColor,
            AutoEllipsis = true
        };

        card.Controls.Add(captionLabel);
        card.Controls.Add(valueLabel);

        return card;
    }

    private static void AddItems(
    ListBox list,
    string category,
    System.Collections.Generic.IEnumerable<string> files)
{
    var items = new System.Collections.Generic.List<string>(files);

    if (items.Count == 0)
        return;

    list.Items.Add(
        $"{category} ({items.Count:N0})");

    foreach (string file in items)
        list.Items.Add("  • " + file);
}

    private static string GetTargetName(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return "-";

        string trimmed = targetPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        string name = Path.GetFileName(trimmed);

        return string.IsNullOrWhiteSpace(name)
            ? targetPath
            : name;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0)
            return "-";

        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        double value = bytes;
        int index = 0;

        while (value >= 1024 &&
               index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours:00}:" +
                   $"{elapsed.Minutes:00}:" +
                   $"{elapsed.Seconds:00}";
        }

        return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
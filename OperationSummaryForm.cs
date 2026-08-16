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
        ClientSize = new Size(430, 300);
        BackColor = Color.FromArgb(244, 247, 250);

        var title = new Label {
            Text = english ? "Operation completed" : "İşlem tamamlandı",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 42, 61),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        Controls.Add(title);

        var body = new Label {
            Text = english
                ? $"Files: {result.TotalFiles}\r\nTotal size: {FormatSize(result.TotalBytes)}\r\nSuccessful: {result.Successful}\r\nFailed: {result.Failed}\r\nVerified: {result.Verified}\r\n\r\nMethod: AES-256-GCM + SHA-256 verification"
                : $"Dosyalar: {result.TotalFiles}\r\nToplam boyut: {FormatSize(result.TotalBytes)}\r\nBaşarılı: {result.Successful}\r\nBaşarısız: {result.Failed}\r\nDoğrulanan: {result.Verified}\r\n\r\nYöntem: AES-256-GCM + SHA-256 doğrulama",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(55, 69, 82),
            AutoSize = true,
            Location = new Point(30, 72)
        };
        Controls.Add(body);

        var close = new Button {
            Text = english ? "Close" : "Kapat",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.System,
            Size = new Size(100, 34),
            Location = new Point(300, 245)
        };
        Controls.Add(close);
        AcceptButton = close;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double n = bytes;
        int i = 0;
        while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
        return $"{n:0.##} {units[i]}";
    }
}

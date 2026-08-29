using System.Drawing;
using System.Windows.Forms;

namespace VoidErase;

internal sealed class VoidEraseSettingsForm : Form
{
    public VoidEraseSettingsForm(bool english)
    {
        Text = english ? "Settings" : "Ayarlar";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(390, 278);
        BackColor = Color.FromArgb(244, 247, 250);
        Padding = new Padding(0);

        var title = new Label
        {
            Text = english ? "VoidErase Settings" : "VoidErase Ayarları",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 42, 61),
            AutoSize = true,
            Location = new Point(18, 12)
        };
        Controls.Add(title);

        var ask = new CheckBox
        {
            Text = english ? "Ask before permanent deletion" : "Kalıcı silmeden önce onay iste",
            Checked = VoidEraseSettings.AskBeforeDeletion,
            AutoSize = true,
            Location = new Point(20, 46)
        };

        var protect = new CheckBox
        {
            Text = english ? "Protect Windows and Program Files" : "Windows ve Program Files klasörlerini koru",
            Checked = VoidEraseSettings.ProtectSystemPaths,
            AutoSize = true,
            Location = new Point(20, 70)
        };

        var hidden = new CheckBox
        {
            Text = english ? "Delete hidden files" : "Gizli dosyaları sil",
            Checked = VoidEraseSettings.DeleteHiddenFiles,
            AutoSize = true,
            Location = new Point(20, 94)
        };

        var updates = new CheckBox
        {
            Text = english ? "Check for updates on startup" : "Başlangıçta güncellemeleri kontrol et",
            Checked = VoidEraseSettings.CheckUpdatesOnStartup,
            AutoSize = true,
            Location = new Point(20, 118)
        };

        var logs = new CheckBox
        {
            Text = english ? "Keep operation logs" : "İşlem günlüklerini tut",
            Checked = VoidEraseSettings.KeepLogs,
            AutoSize = true,
            Location = new Point(20, 142)
        };

        Controls.AddRange(new Control[] { ask, protect, hidden, updates, logs });

        var safetyNote = new Label
        {
            Text = english
                ? "Safety: system paths remain protected by default. File-level verification does not claim physical media sanitization."
                : "Güvenlik: sistem yolları varsayılan olarak korunur. Dosya düzeyi doğrulama, fiziksel medya sanitizasyonu garantisi değildir.",
            Location = new Point(20, 171),
            Size = new Size(350, 48),
            ForeColor = Color.FromArgb(101, 115, 130),
            Font = new Font("Segoe UI", 8F),
            AutoSize = false
        };
        Controls.Add(safetyNote);

        var save = new Button
        {
            Text = english ? "Save" : "Kaydet",
            DialogResult = DialogResult.OK,
            Size = new Size(100, 34),
            Location = new Point(278, 232)
        };
        save.Click += (_, _) =>
        {
            VoidEraseSettings.AskBeforeDeletion = ask.Checked;
            VoidEraseSettings.ProtectSystemPaths = protect.Checked;
            VoidEraseSettings.CheckUpdatesOnStartup = updates.Checked;
            VoidEraseSettings.KeepLogs = logs.Checked;
            VoidEraseSettings.DeleteHiddenFiles = hidden.Checked;
        };
        Controls.Add(save);
        AcceptButton = save;
    }
}

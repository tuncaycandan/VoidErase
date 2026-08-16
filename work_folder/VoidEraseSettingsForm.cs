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
        ClientSize = new Size(420, 330);
        BackColor = Color.FromArgb(244, 247, 250);

        var title = new Label {
            Text = english ? "VoidErase Settings" : "VoidErase Ayarları",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 42, 61),
            AutoSize = true,
            Location = new Point(25, 20)
        };
        Controls.Add(title);

        var ask = new CheckBox {
            Text = english ? "Ask before permanent deletion" : "Kalıcı silmeden önce onay iste",
            Checked = VoidEraseSettings.AskBeforeDeletion,
            AutoSize = true,
            Location = new Point(28, 75)
        };
        var protect = new CheckBox {
            Text = english ? "Protect Windows and Program Files" : "Windows ve Program Files klasörlerini koru",
            Checked = VoidEraseSettings.ProtectSystemPaths,
            AutoSize = true,
            Location = new Point(28, 115)
        };
        var updates = new CheckBox {
            Text = english ? "Check for updates on startup" : "Başlangıçta güncellemeleri kontrol et",
            Checked = VoidEraseSettings.CheckUpdatesOnStartup,
            AutoSize = true,
            Location = new Point(28, 155)
        };
        var logs = new CheckBox {
            Text = english ? "Keep operation logs" : "İşlem günlüklerini tut",
            Checked = VoidEraseSettings.KeepLogs,
            AutoSize = true,
            Location = new Point(28, 195)
        };
        Controls.AddRange(new Control[] { ask, protect, updates, logs });

        var save = new Button {
            Text = english ? "Save" : "Kaydet",
            DialogResult = DialogResult.OK,
            Size = new Size(100, 34),
            Location = new Point(290, 265)
        };
        save.Click += (_, _) => {
            VoidEraseSettings.AskBeforeDeletion = ask.Checked;
            VoidEraseSettings.ProtectSystemPaths = protect.Checked;
            VoidEraseSettings.CheckUpdatesOnStartup = updates.Checked;
            VoidEraseSettings.KeepLogs = logs.Checked;
        };
        Controls.Add(save);
        AcceptButton = save;
    }
}

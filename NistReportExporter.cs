using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace VoidErase;

internal static class NistReportExporter
{
    internal static string ExportHtml(string xmlPath, bool english)
    {
        if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
            throw new FileNotFoundException(english ? "NIST XML record was not found." : "NIST XML kaydı bulunamadı.", xmlPath);

        NistSanitizationRecord record = NistSanitizationRecordStore.Load(xmlPath);
        string output = Path.ChangeExtension(xmlPath, ".html");
        string title = english ? "VoidErase NIST Sanitization Record" : "VoidErase NIST Sanitizasyon Kaydı";
        string language = english ? "en" : "tr";
        StringBuilder html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(language).Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(E(title)).Append("</title><style>");
        html.Append("body{font-family:Segoe UI,Arial,sans-serif;background:#f3f6f9;color:#1f2a34;margin:0;padding:36px}main{max-width:960px;margin:auto;background:#fff;border:1px solid #d6dee7;border-radius:10px;padding:32px;box-shadow:0 4px 18px #152b3d18}h1,h2{color:#126fa3}h2{border-bottom:2px solid #e5edf3;padding-bottom:8px;margin-top:28px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}.item{background:#f7f9fb;border:1px solid #e1e7ed;padding:10px;border-radius:5px}.label{font-size:11px;color:#657382;font-weight:700;text-transform:uppercase}.value{margin-top:4px;word-break:break-word}.badge{font-weight:700;color:#18733d}footer{margin-top:28px;border-top:1px solid #e1e7ed;padding-top:12px;color:#657382;font-size:12px}@media print{body{background:#fff;padding:0}main{border:0;box-shadow:none;max-width:none}}</style></head><body><main>");
        html.Append("<h1>").Append(E(title)).Append("</h1><p>").Append(E(english ? "NIST SP 800-88 Rev. 2 aligned application record" : "NIST SP 800-88 Rev. 2 uyumlu uygulama kaydı")).Append("</p><p class=\"badge\">").Append(E(record.Outcome)).Append("</p>");
        Section(html, english ? "Record summary" : "Kayıt özeti", new[] {
            Pair(english ? "Record ID" : "Kayıt ID", record.RecordId), Pair(english ? "Standard" : "Standart", record.Standard),
            Pair(english ? "Technique" : "Teknik", record.Technique), Pair(english ? "Method" : "Yöntem", record.Method),
            Pair(english ? "Assurance" : "Güvence", record.Assurance), Pair(english ? "Compatibility" : "Uyumluluk", record.Compatibility),
            Pair(english ? "Validation required" : "Validasyon gerekli", record.ValidationRequiredText), Pair(english ? "Decision reason" : "Karar nedeni", record.DecisionReason), Pair(english ? "Language" : "Dil", record.Language), Pair(english ? "Provider" : "Sağlayıcı", record.ProviderName), Pair(english ? "Provider version" : "Sağlayıcı sürümü", record.ProviderVersion), Pair(english ? "Evidence path" : "Kanıt yolu", record.EvidencePath), Pair(english ? "Identity validation" : "Kimlik doğrulama", record.IdentityValidation)
        });
        Section(html, english ? "Media identity" : "Medya kimliği", new[] {
            Pair(english ? "Target path" : "Hedef yolu", record.Media.TargetPath), Pair(english ? "Physical drive" : "Fiziksel aygıt", record.Media.PhysicalDrive),
            Pair(english ? "Disk number" : "Disk numarası", record.Media.DiskNumber), Pair("Model", record.Media.Model),
            Pair(english ? "Serial number" : "Seri numarası", record.Media.SerialNumber), Pair(english ? "Media type" : "Medya türü", record.Media.MediaType),
            Pair(english ? "Bus type" : "Bağlantı türü", record.Media.BusType), Pair(english ? "Size (bytes)" : "Boyut (bayt)", record.Media.SizeBytes.ToString("N0"))
        });
        Section(html, english ? "Identity validation" : "Kimlik doğrulama", new[] {
            Pair(english ? "Status" : "Durum", record.IdentityValidation),
            Pair(english ? "Identity match" : "Kimlik eşleşmesi", record.IdentityMatch ? (english ? "Yes" : "Evet") : (english ? "No" : "Hayır")),
            Pair(english ? "Pre-operation identity" : "İşlem öncesi kimlik", SnapshotSummary(record.PreOperationIdentity)),
            Pair(english ? "Post-operation identity" : "İşlem sonrası kimlik", SnapshotSummary(record.PostOperationIdentity))
        });
        Section(html, english ? "Verification" : "Doğrulama", new[] {
            Pair(english ? "Outcome" : "Sonuç", record.Verification.Outcome), Pair(english ? "Method" : "Yöntem", record.Verification.Method),
            Pair(english ? "Evidence" : "Kanıt", record.Verification.Evidence), Pair(english ? "Details" : "Ayrıntılar", record.Verification.Details),
            Pair(english ? "Files" : "Dosyalar", record.TotalFiles.ToString("N0")), Pair(english ? "Total bytes" : "Toplam bayt", record.TotalBytes.ToString("N0"))
        });
        html.Append("<h2>").Append(E(english ? "Limitations" : "Sınırlamalar")).Append("</h2><p>").Append(E(record.ClaimLimitation)).Append("</p><footer>").Append(E(record.OperatorNote)).Append("</footer></main></body></html>");
        File.WriteAllText(output, html.ToString(), new UTF8Encoding(false));
        return output;
    }

    internal static string ExportPdf(string htmlPath, bool english)
    {
        if (string.IsNullOrWhiteSpace(htmlPath) || !File.Exists(htmlPath))
            throw new FileNotFoundException(english ? "HTML report was not found." : "HTML raporu bulunamadı.", htmlPath);
        string browser = FindBrowser();
        if (browser == null) throw new InvalidOperationException(english ? "Microsoft Edge or Chrome was not found." : "Microsoft Edge veya Chrome bulunamadı.");
        string pdf = Path.ChangeExtension(htmlPath, ".pdf");
        using (Process p = Process.Start(new ProcessStartInfo { FileName = browser, Arguments = "--headless --disable-gpu --no-pdf-header-footer --print-to-pdf=\"" + pdf + "\" \"" + htmlPath + "\"", UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
        {
            if (p == null || !p.WaitForExit(30000)) throw new IOException(english ? "PDF report could not be created." : "PDF raporu oluşturulamadı.");
        }
        if (!File.Exists(pdf)) throw new IOException(english ? "PDF report could not be created." : "PDF raporu oluşturulamadı.");
        return pdf;
    }

    private static string FindBrowser()
    {
        string[] paths = { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft\\Edge\\Application\\msedge.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft\\Edge\\Application\\msedge.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google\\Chrome\\Application\\chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google\\Chrome\\Application\\chrome.exe") };
        foreach (string path in paths) if (File.Exists(path)) return path;
        return null;
    }

    private static void Section(StringBuilder html, string title, string[] pairs)
    {
        html.Append("<h2>").Append(E(title)).Append("</h2><div class=\"grid\">");
        foreach (string pair in pairs) html.Append(pair);
        html.Append("</div>");
    }

    private static string Pair(string label, string value) { return "<div class=\"item\"><div class=\"label\">" + E(label) + "</div><div class=\"value\">" + E(value) + "</div></div>"; }

    private static string SnapshotSummary(SanitizationIdentitySnapshot snapshot)
    {
        if (snapshot == null) return "Not available";
        return (snapshot.PhysicalDrive ?? "") + " / " +
               (snapshot.DiskNumber ?? "") + " / " +
               (snapshot.Model ?? "") + " / " +
               (snapshot.SerialNumber ?? "") + " / " +
               snapshot.SizeBytes.ToString("N0");
    }

    private static string E(string value) { return WebUtility.HtmlEncode(value ?? ""); }
}

internal interface ISanitizationProvider
{
    string ProviderName { get; }
    string ProviderVersion { get; }
    bool PhysicalWriteAuthorized { get; }
    bool CanHandle(string mediaType);
    string PrepareDryRun(string targetPath);
}

internal sealed class DryRunSanitizationProvider : ISanitizationProvider
{
    public string ProviderName { get { return "VoidErase dry-run provider"; } }
    public string ProviderVersion { get { return "1.4.0"; } }
    public bool PhysicalWriteAuthorized { get { return false; } }
    public bool CanHandle(string mediaType) { return !string.IsNullOrWhiteSpace(mediaType); }
    public string PrepareDryRun(string targetPath) { return "DRY-RUN ONLY: no physical device write is authorized."; }
}

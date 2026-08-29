using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace VoidErase;

public enum NistSanitizationOutcome
{
    NotEstablished,
    Succeeded,
    Failed,
    Blocked,
    Cancelled
}

public enum NistVerificationOutcome
{
    NotPerformed,
    Passed,
    Failed,
    NotApplicable
}

public sealed class NistMediaIdentity
{
    public string TargetPath { get; set; } = "";
    public string PhysicalDrive { get; set; } = "";
    public string DiskNumber { get; set; } = "";
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string BusType { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsSystemDisk { get; set; }
    public bool IsBootDisk { get; set; }
}

public sealed class NistVerificationRecord
{
    [XmlIgnore]
    public NistVerificationOutcome OutcomeCode { get; set; }
    public string Outcome { get; set; } = "";
    public string Method { get; set; } = "";
    public string Evidence { get; set; } = "";
    public DateTime CompletedAtUtc { get; set; }
    public string Details { get; set; } = "";
}

[Serializable]
public sealed class NistSanitizationRecord
{
    public string RecordId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    [XmlIgnore]
    public NistSanitizationOutcome OutcomeCode { get; set; }
    public string Outcome { get; set; } = "";
    public string Standard { get; set; } = "NIST SP 800-88 Rev. 2 aligned record";
    public string Technique { get; set; } = "";
    public string Method { get; set; } = "";
    public string Assurance { get; set; } = "";
    public bool ClaimAllowed { get; set; }
    public string ClaimLimitation { get; set; } = "";
    public string Compatibility { get; set; } = "NotEstablished";
    public bool ValidationRequired { get; set; }
    public string ValidationRequiredText { get; set; } = "";
    public string DecisionReason { get; set; } = "";
    public string Language { get; set; } = "tr";
    public string ProviderName { get; set; } = "";
    public string ProviderVersion { get; set; } = "";
    public string EvidencePath { get; set; } = "";
    public bool IdentityMatch { get; set; }
    public string IdentityValidation { get; set; } = "";
    public SanitizationIdentitySnapshot PreOperationIdentity { get; set; }
    public SanitizationIdentitySnapshot PostOperationIdentity { get; set; }
    public NistMediaIdentity Media { get; set; } = new NistMediaIdentity();
    public NistVerificationRecord Verification { get; set; } = new NistVerificationRecord();
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public int SuccessfulFiles { get; set; }
    public int FailedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public string OperatorNote { get; set; } = "";
}

internal static class NistSanitizationRecordFactory
{
    internal static NistSanitizationRecord FromOperationResult(OperationResult result, bool english)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        bool succeeded = !result.Cancelled && result.Failed == 0 && result.Skipped == 0;
        NistMediaIdentity media = CreateMediaIdentity(result.TargetPath, english);
        MediaInfo mediaInfo = null;
        SanitizationDecision decision = null;
        try
        {
            mediaInfo = MediaDetection.Detect(result.TargetPath);
            decision = NistSanitization.Decide(mediaInfo);
        }
        catch
        {
            // Unknown media keeps the record conservative and non-claimable.
        }

        return new NistSanitizationRecord
        {
            StartedAtUtc = result.StartedAt.ToUniversalTime(),
            CompletedAtUtc = DateTime.UtcNow,
            OutcomeCode = result.Cancelled
                ? NistSanitizationOutcome.Cancelled
                : (succeeded ? NistSanitizationOutcome.Succeeded : NistSanitizationOutcome.Failed),
            Outcome = LocalizeOutcome(result.Cancelled
                ? NistSanitizationOutcome.Cancelled
                : (succeeded ? NistSanitizationOutcome.Succeeded : NistSanitizationOutcome.Failed), english),
            Standard = english ? "NIST SP 800-88 Rev. 2 aligned record" : "NIST SP 800-88 Rev. 2 uyumlu kayıt",
            Technique = decision == null ? (english ? "Logical file-level cryptographic transformation" : "Mantıksal dosya düzeyinde kriptografik dönüşüm") : LocalizeDecisionTechnique(decision, english),
            Method = decision == null ? (english ? "Clear (application scope only)" : "Clear (yalnızca uygulama kapsamı)") : LocalizeDecisionMethod(decision, english),
            Assurance = decision == null ? (english ? "Application-level verification" : "Uygulama düzeyi doğrulama") : LocalizeAssurance(decision.Assurance, english),
            Compatibility = decision == null ? (english ? "Not established" : "Belirlenmedi") : LocalizeCompatibility(decision.Compatibility, english),
            ValidationRequired = decision == null || decision.ValidationRequired,
            ValidationRequiredText = (decision == null || decision.ValidationRequired)
                ? (english ? "Yes" : "Evet")
                : (english ? "No" : "Hayır"),
            DecisionReason = decision == null ? (english ? "Media decision was not available." : "Medya kararı alınamadı.") : LocalizeDecisionReason(decision, english),
            Language = english ? "en" : "tr",
            ProviderName = english ? "VoidErase dry-run provider" : "VoidErase dry-run sağlayıcısı",
            ProviderVersion = "1.4.0",
            EvidencePath = "",
            IdentityMatch = result.IdentityMatch,
            IdentityValidation = string.IsNullOrWhiteSpace(result.IdentityValidation)
                ? (english ? "Pre/post device identity comparison was not performed." : "İşlem öncesi/sonrası aygıt kimliği karşılaştırması uygulanmadı.")
                : result.IdentityValidation,
            PreOperationIdentity = result.PreOperationIdentity,
            PostOperationIdentity = result.PostOperationIdentity,
            ClaimAllowed = false,
            ClaimLimitation = english ? "This record does not claim physical-media Purge or Destroy. It records only application-level processing and verification." : "Bu kayıt fiziksel medya Purge veya Destroy iddiasında bulunmaz. Yalnızca uygulama düzeyi işleme ve doğrulamayı kaydeder.",
            Media = media,

            Verification = new NistVerificationRecord
            {
                OutcomeCode = result.VerificationCompleted
                    ? NistVerificationOutcome.Passed
                    : NistVerificationOutcome.NotPerformed,
                Outcome = LocalizeVerificationOutcome(
                    result.VerificationCompleted ? NistVerificationOutcome.Passed : NistVerificationOutcome.NotPerformed,
                    english),
                Method = english ? (result.VerificationMethod ?? "") : "AES-256-GCM + SHA-256",
                Evidence = result.VerificationCompleted ? (english ? "Per-file verification completed." : "Dosya başına doğrulama tamamlandı.") : "",
                CompletedAtUtc = DateTime.UtcNow,
                Details = english ? "See operation summary and application log for per-file details." : "Dosya başına ayrıntılar için işlem özetine ve uygulama günlüğüne bakın."
            },
            TotalFiles = result.TotalFiles,
            TotalBytes = result.TotalBytes,
            SuccessfulFiles = result.Successful,
            FailedFiles = result.Failed,
            SkippedFiles = result.Skipped,
            OperatorNote = english
                ? "Generated by VoidErase " + GetDisplayVersion() + "; physical device sanitization was not performed."
                : "VoidErase " + GetDisplayVersion() + " tarafından oluşturuldu; fiziksel aygıt sanitizasyonu yapılmadı."
        };
    }

    private static string GetDisplayVersion()
    {
        object[] attributes = Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
        AssemblyInformationalVersionAttribute informational = attributes.Length > 0
            ? attributes[0] as AssemblyInformationalVersionAttribute
            : null;
        string version = informational == null ? null : informational.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = Assembly.GetExecutingAssembly().GetName().Version == null
                ? "0.0.0"
                : Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        return "v" + version.TrimStart('v', 'V');
    }

    private static string LocalizeOutcome(NistSanitizationOutcome outcome, bool english)
    {
        if (english)
            return outcome == NistSanitizationOutcome.Succeeded ? "Succeeded" :
                   outcome == NistSanitizationOutcome.Failed ? "Failed" :
                   outcome == NistSanitizationOutcome.Cancelled ? "Cancelled" :
                   outcome == NistSanitizationOutcome.Blocked ? "Blocked" : "Not established";
        return outcome == NistSanitizationOutcome.Succeeded ? "Başarılı" :
               outcome == NistSanitizationOutcome.Failed ? "Başarısız" :
               outcome == NistSanitizationOutcome.Cancelled ? "İptal edildi" :
               outcome == NistSanitizationOutcome.Blocked ? "Engellendi" : "Belirlenmedi";
    }

    private static string LocalizeVerificationOutcome(NistVerificationOutcome outcome, bool english)
    {
        if (english)
            return outcome == NistVerificationOutcome.Passed ? "Passed" :
                   outcome == NistVerificationOutcome.Failed ? "Failed" :
                   outcome == NistVerificationOutcome.NotApplicable ? "Not applicable" : "Not performed";
        return outcome == NistVerificationOutcome.Passed ? "Başarılı" :
               outcome == NistVerificationOutcome.Failed ? "Başarısız" :
               outcome == NistVerificationOutcome.NotApplicable ? "Uygulanamaz" : "Yapılmadı";
    }

    private static string LocalizeAssurance(SanitizationAssurance assurance, bool english)
    {
        if (english)
            return assurance == SanitizationAssurance.High ? "High" :
                   assurance == SanitizationAssurance.Enhanced ? "Enhanced" :
                   assurance == SanitizationAssurance.Basic ? "Basic" : "None";
        return assurance == SanitizationAssurance.High ? "Yüksek" :
               assurance == SanitizationAssurance.Enhanced ? "Geliştirilmiş" :
               assurance == SanitizationAssurance.Basic ? "Temel" : "Yok";
    }

    private static string LocalizeCompatibility(NistCompatibilityState state, bool english)
    {
        if (english)
            return state == NistCompatibilityState.Verified ? "Verified" :
                   state == NistCompatibilityState.Candidate ? "Candidate" :
                   state == NistCompatibilityState.Blocked ? "Blocked" : "Not established";
        return state == NistCompatibilityState.Verified ? "Doğrulandı" :
               state == NistCompatibilityState.Candidate ? "Aday" :
               state == NistCompatibilityState.Blocked ? "Engellendi" : "Belirlenmedi";
    }

    private static NistMediaIdentity CreateMediaIdentity(string targetPath, bool english)
    {
        NistMediaIdentity identity = new NistMediaIdentity
        {
            TargetPath = XmlSafe(targetPath ?? ""),
            MediaType = english ? "Not evaluated" : "Değerlendirilmedi",
            BusType = english ? "Not evaluated" : "Değerlendirilmedi"
        };

        try
        {
            SanitizationPlan plan = StorageSanitizationProtocol.AnalyzePath(targetPath);
            identity.PhysicalDrive = XmlSafe(plan.PhysicalDrive ?? "");
            identity.DiskNumber = XmlSafe(plan.DiskNumber ?? "");
            identity.Model = XmlSafe(plan.Model ?? "");
            identity.SerialNumber = XmlSafe(plan.SerialNumber ?? "");
            identity.MediaType = LocalizeMediaType(plan, english);
            identity.BusType = XmlSafe(plan.BusType ?? "");
            identity.IsSystemDisk = plan.IsSystemDisk;
            identity.SizeBytes = TryGetPhysicalDiskSize(plan.PhysicalDrive);
            if (identity.SizeBytes <= 0)
                identity.SizeBytes = TryGetVolumeSize(targetPath);
        }
        catch
        {
            // A missing identity must remain explicit in the record; it must
            // never be replaced with guessed or stale device information.
        }

        return identity;
    }

    private static string XmlSafe(string value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        StringBuilder builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            // XML 1.0 permits tab, LF, CR and characters from U+0020 onward,
            // excluding surrogate code units handled here conservatively.
            if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF))
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static string LocalizeDecisionTechnique(SanitizationDecision decision, bool english)
    {
        if (english) return decision.Technique;
        if (decision.Method == SanitizationMethod.Clear) return "Mantıksal Clear işlemi";
        if (decision.Method == SanitizationMethod.Purge) return "Aygıt destekli Purge gereklidir";
        if (decision.Method == SanitizationMethod.Destroy) return "Fiziksel Destroy işlemi gereklidir";
        return "Sanitizasyon yöntemi belirlenmedi";
    }

    private static string LocalizeDecisionMethod(SanitizationDecision decision, bool english)
    {
        if (english) return decision.MethodName;
        if (decision.Method == SanitizationMethod.Clear) return "Clear (uygulama kapsamı)";
        if (decision.Method == SanitizationMethod.Purge) return "Purge (kanıt olmadan oluşturulmadı)";
        if (decision.Method == SanitizationMethod.Destroy) return "Destroy (fiziksel işlem gerektirir)";
        return "Belirlenmedi";
    }

    private static string LocalizeDecisionReason(SanitizationDecision decision, bool english)
    {
        if (decision.Compatibility == NistCompatibilityState.Blocked)
            return english
                ? "The target is blocked by a mandatory safety rule, including system or boot-disk protection."
                : "Hedef, sistem veya boot diski koruması gibi zorunlu güvenlik kuralları nedeniyle engellendi."
                    + " Dosya düzeyi işlem fiziksel disk sanitizasyonu değildir.";

        return english ? decision.Reason :
            (decision.Media == MediaKind.SolidState ? "SSD ortamında yeniden eşlenen ve ayrılmış alanlar bulunabilir." :
             decision.Media == MediaKind.Removable ? "USB bellekte dosya sistemi dışında kalan alanlar bulunabilir." :
             decision.Media == MediaKind.Magnetic ? "Manyetik ortam Clear için aday olarak değerlendirildi." :
             "Medya için güvenli sanitizasyon kararı oluşturulamadı.");
    }

    private static long TryGetPhysicalDiskSize(string physicalDrive)
    {
        if (string.IsNullOrWhiteSpace(physicalDrive)) return 0L;
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "root\\cimv2", "SELECT DeviceID, Size FROM Win32_DiskDrive"))
            using (ManagementObjectCollection disks = searcher.Get())
            {
                foreach (ManagementObject disk in disks)
                {
                    string deviceId = Convert.ToString(disk["DeviceID"]) ?? "";
                    if (string.Equals(deviceId, physicalDrive, StringComparison.OrdinalIgnoreCase))
                        return ToInt64(disk["Size"]);
                }
            }
        }
        catch { }
        return 0L;
    }

    private static long TryGetVolumeSize(string targetPath)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(targetPath));
            if (!string.IsNullOrWhiteSpace(root))
                return new DriveInfo(root).TotalSize;
        }
        catch { }
        return 0L;
    }

    private static long ToInt64(object value)
    {
        long result;
        return long.TryParse(Convert.ToString(value), out result) ? result : 0L;
    }

    private static string LocalizeMediaType(SanitizationPlan plan, bool english)
    {
        if (plan == null) return english ? "Not evaluated" : "Değerlendirilmedi";
        switch (plan.MediaKind)
        {
            case StorageMediaKind.Hdd: return english ? "HDD" : "HDD";
            case StorageMediaKind.SataSsd: return english ? "SATA SSD" : "SATA SSD";
            case StorageMediaKind.Nvme: return english ? "NVMe SSD" : "NVMe SSD";
            case StorageMediaKind.UsbFlash: return english ? "USB flash" : "USB bellek";
            case StorageMediaKind.Virtual: return english ? "Virtual disk" : "Sanal disk";
            default:
                return string.IsNullOrWhiteSpace(plan.WindowsMediaType)
                    ? (english ? "Unknown" : "Bilinmiyor")
                    : XmlSafe(plan.WindowsMediaType);
        }
    }
}

internal static class NistSanitizationRecordStore
{
    private static readonly object Sync = new object();

    internal static string RecordDirectory
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoidErase", "NistRecords");
        }
    }

    internal static string Save(NistSanitizationRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        Directory.CreateDirectory(RecordDirectory);

        string safeId = string.IsNullOrWhiteSpace(record.RecordId)
            ? Guid.NewGuid().ToString("N")
            : record.RecordId;
        record.RecordId = safeId;
        string path = Path.Combine(RecordDirectory, "NIST-" + safeId + ".xml");
        string temporary = path + ".tmp";

        XmlSerializer serializer = new XmlSerializer(typeof(NistSanitizationRecord));
        lock (Sync)
        {
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(stream, record);
                stream.Flush(true);
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
        return path;
    }

    internal static NistSanitizationRecord Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        XmlSerializer serializer = new XmlSerializer(typeof(NistSanitizationRecord));
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            return (NistSanitizationRecord)serializer.Deserialize(stream);
    }
}

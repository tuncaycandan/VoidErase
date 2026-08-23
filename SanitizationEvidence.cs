using System;
using System.IO;
using System.Xml.Serialization;

namespace VoidErase;

[Serializable]
public sealed class SanitizationIdentitySnapshot
{
    public string PhysicalDrive { get; set; } = "";
    public string DiskNumber { get; set; } = "";
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string BusType { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsSystemDisk { get; set; }
    public bool IsBootDisk { get; set; }

    internal static SanitizationIdentitySnapshot FromMedia(NistMediaIdentity media)
    {
        if (media == null) return new SanitizationIdentitySnapshot();
        return new SanitizationIdentitySnapshot
        {
            PhysicalDrive = media.PhysicalDrive ?? "",
            DiskNumber = media.DiskNumber ?? "",
            Model = media.Model ?? "",
            SerialNumber = media.SerialNumber ?? "",
            MediaType = media.MediaType ?? "",
            BusType = media.BusType ?? "",
            SizeBytes = media.SizeBytes,
            IsSystemDisk = media.IsSystemDisk,
            IsBootDisk = media.IsBootDisk
        };
    }

    internal bool Matches(SanitizationIdentitySnapshot other)
    {
        if (other == null || IsSystemDisk || IsBootDisk || other.IsSystemDisk || other.IsBootDisk) return false;
        return Same(PhysicalDrive, other.PhysicalDrive) && Same(DiskNumber, other.DiskNumber) &&
               Same(Model, other.Model) && Same(SerialNumber, other.SerialNumber) &&
               Same(MediaType, other.MediaType) && Same(BusType, other.BusType) &&
               SizeBytes > 0 && SizeBytes == other.SizeBytes;
    }

    private static bool Same(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

[Serializable]
public sealed class SanitizationEvidence
{
    public string ProviderName { get; set; } = "";
    public string ProviderVersion { get; set; } = "";
    public string EvidenceId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CompletedAtUtc { get; set; }
    public string ClaimedMethod { get; set; } = "";
    public string ClaimedOutcome { get; set; } = "";
    public string TargetPhysicalDrive { get; set; } = "";
    public string TargetSerialNumber { get; set; } = "";
    public long TargetSizeBytes { get; set; }
    public string EvidenceHashSha256 { get; set; } = "";
    public bool ProviderVerified { get; set; }
}

internal sealed class SanitizationEvidenceImportResult
{
    public bool Accepted { get; set; }
    public string Message { get; set; } = "";
    public SanitizationEvidence Evidence { get; set; }
}

internal static class SanitizationEvidenceImporter
{
    internal static SanitizationEvidenceImportResult Import(string path, SanitizationIdentitySnapshot expected, bool english)
    {
        SanitizationEvidenceImportResult result = new SanitizationEvidenceImportResult();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            result.Message = english ? "Evidence file was not found." : "Kanıt dosyası bulunamadı.";
            return result;
        }

        try
        {
            SanitizationEvidence evidence;
            XmlSerializer serializer = new XmlSerializer(typeof(SanitizationEvidence));
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                evidence = (SanitizationEvidence)serializer.Deserialize(stream);

            bool valid = evidence != null && evidence.ProviderVerified &&
                         !string.IsNullOrWhiteSpace(evidence.ProviderName) &&
                         !string.IsNullOrWhiteSpace(evidence.TargetPhysicalDrive) &&
                         !string.IsNullOrWhiteSpace(evidence.TargetSerialNumber) &&
                         evidence.TargetSizeBytes > 0 && expected != null &&
                         string.Equals(evidence.TargetPhysicalDrive, expected.PhysicalDrive, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(evidence.TargetSerialNumber, expected.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
                         evidence.TargetSizeBytes == expected.SizeBytes &&
                         !expected.IsSystemDisk && !expected.IsBootDisk;

            result.Accepted = valid;
            result.Evidence = valid ? evidence : null;
            result.Message = valid
                ? (english ? "Provider evidence accepted." : "Sağlayıcı kanıtı kabul edildi.")
                : (english ? "Evidence rejected: identity or safety validation failed." : "Kanıt reddedildi: kimlik veya güvenlik doğrulaması başarısız.");
            return result;
        }
        catch (Exception ex)
        {
            result.Message = (english ? "Evidence could not be read: " : "Kanıt okunamadı: ") + ex.Message;
            return result;
        }
    }
}

internal sealed class SafeProviderPlan
{
    public string ProviderName { get; set; } = "VoidErase dry-run provider";
    public bool DryRunOnly { get; set; } = true;
    public bool PhysicalWriteAuthorized { get; set; } = false;
    public string Description { get; set; } = "No physical device command is executed.";
}

internal static class SafeProviderFactory
{
    internal static SafeProviderPlan CreateDryRunPlan(SanitizationIdentitySnapshot identity, bool english)
    {
        if (identity == null || identity.IsSystemDisk || identity.IsBootDisk)
            return new SafeProviderPlan { Description = english ? "Blocked system or boot disk." : "Sistem veya boot diski engellendi." };
        return new SafeProviderPlan { Description = english ? "Dry-run only; no physical device write is authorized." : "Yalnızca dry-run; fiziksel aygıta yazma yetkisi yok." };
    }
}

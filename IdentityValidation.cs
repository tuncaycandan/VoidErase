using System;

namespace VoidErase;

internal sealed class IdentityComparisonResult
{
    public bool Match { get; set; }
    public string Status { get; set; } = "Not available";
    public string Details { get; set; } = "";
}

internal static class MediaIdentityValidation
{
    internal static SanitizationIdentitySnapshot Capture(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) ||
            targetPath.Equals("Multiple items", StringComparison.OrdinalIgnoreCase) ||
            targetPath.Equals("Birden fazla öğe", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            UnifiedStoragePreflightResult preflight = UnifiedStoragePreflight.AnalyzePath(targetPath);
            NistMediaIdentity media = new NistMediaIdentity
            {
                PhysicalDrive = preflight.PhysicalDrive ?? "",
                DiskNumber = preflight.DiskNumber >= 0 ? preflight.DiskNumber.ToString() : "",
                Model = preflight.Model ?? "",
                SerialNumber = preflight.SerialNumber ?? "",
                MediaType = preflight.WindowsMediaType ?? preflight.MediaKind.ToString(),
                BusType = preflight.BusType ?? "",
                SizeBytes = preflight.DiskSizeBytes > 0 ? preflight.DiskSizeBytes : TryGetDriveSize(targetPath),
                IsSystemDisk = preflight.IsSystemDisk,
                IsBootDisk = preflight.IsBootDisk
            };
            return SanitizationIdentitySnapshot.FromMedia(media);
        }
        catch
        {
            return null;
        }
    }

    private static long TryGetDriveSize(string driveRoot)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(driveRoot))
                return new System.IO.DriveInfo(driveRoot).TotalSize;
        }
        catch
        {
        }
        return 0;
    }

    internal static IdentityComparisonResult Compare(
        SanitizationIdentitySnapshot before,
        SanitizationIdentitySnapshot after,
        bool english)
    {
        string notAvailable = english ? "Not available" : "Uygulanamadı";
        string passed = english ? "Passed" : "Başarılı";
        string failed = english ? "Failed" : "Başarısız";
        string details;

        if (before == null || after == null)
        {
            details = english
                ? "Pre/post media identity could not be captured for this target."
                : "Bu hedef için işlem öncesi/sonrası medya kimliği alınamadı.";
            return new IdentityComparisonResult { Match = false, Status = notAvailable, Details = details };
        }

        if (before.IsSystemDisk || before.IsBootDisk || after.IsSystemDisk || after.IsBootDisk)
        {
            details = english
                ? "Identity comparison is not claimable for a system or boot disk."
                : "Sistem veya boot diski için kimlik karşılaştırması iddia edilemez.";
            return new IdentityComparisonResult { Match = false, Status = failed, Details = details };
        }

        bool match = before.Matches(after);
        details = match
            ? (english ? "Pre/post media identity matches." : "İşlem öncesi/sonrası medya kimliği eşleşiyor.")
            : (english ? "Pre/post media identity mismatch detected; possible device change." : "İşlem öncesi/sonrası medya kimliği eşleşmiyor; olası aygıt değişimi tespit edildi.");

        return new IdentityComparisonResult
        {
            Match = match,
            Status = match ? passed : failed,
            Details = details
        };
    }
}

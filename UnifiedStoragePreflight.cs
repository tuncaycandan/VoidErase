using System;
using System.Globalization;

internal enum UnifiedStoragePreflightState
{
    Pass,
    Blocked,
    Error
}

internal sealed class UnifiedStoragePreflightResult
{
    internal UnifiedStoragePreflightState State { get; set; }
    internal string TargetPath { get; set; }
    internal string PhysicalDrive { get; set; }
    internal int DiskNumber { get; set; }
    internal StorageMediaKind MediaKind { get; set; }
    internal string Model { get; set; }
    internal string SerialNumber { get; set; }
    internal string BusType { get; set; }
    internal string WindowsMediaType { get; set; }
    internal long DiskSizeBytes { get; set; }
    internal bool IsSystemDisk { get; set; }
    internal bool IsBootDisk { get; set; }
    internal bool IsOffline { get; set; }
    internal bool IsReadOnly { get; set; }
    internal bool Encrypted { get; set; }
    internal string EncryptionStatus { get; set; }
    internal SanitizationStrength RecommendedStrength { get; set; }
    internal string RecommendedMethod { get; set; }
    internal bool DeviceCommandRequired { get; set; }
    internal DeviceSanitizeCapability DeviceSanitizeCapability { get; set; }
    internal string CapabilityDetail { get; set; }
    internal string Reason { get; set; }
    internal string Scope { get; set; }
}

internal static class UnifiedStoragePreflight
{
    // SINGLE MEDIA-SPECIFIC PREFLIGHT ENTRY POINT.
    // DRY-RUN ONLY: this router never erases, formats, trims, sanitizes,
    // overwrites or issues destructive device IOCTLs.
    internal static UnifiedStoragePreflightResult AnalyzePath(string targetPath)
    {
        UnifiedStoragePreflightResult output = NewResult(targetPath);

        try
        {
            SanitizationPlan plan =
                StorageSanitizationProtocol.AnalyzePath(targetPath);

            CopyPlan(output, plan);

            int diskNumber;
            if (!int.TryParse(plan.DiskNumber, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out diskNumber))
            {
                output.State = UnifiedStoragePreflightState.Blocked;
                output.Reason =
                    "Target could not be mapped to a physical disk.";
                return output;
            }

            return ApplyMediaSpecificGate(output, targetPath, diskNumber);
        }
        catch (Exception ex)
        {
            output.State = UnifiedStoragePreflightState.Error;
            output.Reason = "Unified media preflight failed: " + ex.Message;
            return output;
        }
    }

    internal static UnifiedStoragePreflightResult AnalyzeDisk(int diskNumber)
    {
        UnifiedStoragePreflightResult output = NewResult(
            @"\\.\PHYSICALDRIVE" + diskNumber);

        try
        {
            SanitizationPlan plan =
                StorageSanitizationProtocol.AnalyzePhysicalDiskNumber(
                    diskNumber);

            CopyPlan(output, plan);
            return ApplyMediaSpecificGate(
                output,
                plan.DriveRoot ?? plan.PhysicalDrive,
                diskNumber);
        }
        catch (Exception ex)
        {
            output.State = UnifiedStoragePreflightState.Error;
            output.Reason = "Unified media preflight failed: " + ex.Message;
            return output;
        }
    }

    private static UnifiedStoragePreflightResult ApplyMediaSpecificGate(
        UnifiedStoragePreflightResult output,
        string targetPath,
        int diskNumber)
    {
        if (output.IsSystemDisk)
        {
            output.State = UnifiedStoragePreflightState.Blocked;
            output.Scope = "System disk detected; no execution scope established.";
            output.Reason =
                "Running Windows system disk is blocked from in-OS sanitization.";
            return output;
        }

        if (output.IsBootDisk)
        {
            output.State = UnifiedStoragePreflightState.Blocked;
            output.Reason = "Selected disk is reported as a boot disk.";
            return output;
        }

        if (output.IsOffline)
        {
            output.State = UnifiedStoragePreflightState.Blocked;
            output.Reason = "Selected disk is offline.";
            return output;
        }

        if (output.IsReadOnly)
        {
            output.State = UnifiedStoragePreflightState.Blocked;
            output.Reason = "Selected disk is read-only.";
            return output;
        }

        if (output.MediaKind == StorageMediaKind.Hdd)
            return ApplyHddGate(output, targetPath, diskNumber);

        if (output.MediaKind == StorageMediaKind.UsbFlash)
            return ApplyUsbGate(output, targetPath, diskNumber);

        if (output.MediaKind == StorageMediaKind.Nvme)
            return ApplyNvmeGate(output, diskNumber);

        if (output.MediaKind == StorageMediaKind.SataSsd)
        {
            output.State = UnifiedStoragePreflightState.Pass;
            output.Scope =
                "SATA SSD identified. Media decision is preflight-only; " +
                "no device command is enabled.";
            output.Reason =
                output.RecommendedMethod ??
                "SATA SSD identified successfully.";
            return output;
        }

        output.State = UnifiedStoragePreflightState.Blocked;
        output.Reason =
            "Media type is not sufficiently identified for a unified sanitization decision.";
        return output;
    }

    private static UnifiedStoragePreflightResult ApplyHddGate(
        UnifiedStoragePreflightResult output,
        string targetPath,
        int diskNumber)
    {
        HddLogicalClearPreflightResult preflight =
            HddLogicalClearPreflight.AnalyzeDisk(diskNumber);

        if (preflight == null)
        {
            output.State = UnifiedStoragePreflightState.Error;
            output.Reason = "HDD preflight returned no result.";
            return output;
        }

        output.Model = preflight.Model ?? output.Model;
        output.SerialNumber = preflight.SerialNumber ?? output.SerialNumber;
        output.BusType = preflight.BusType ?? output.BusType;
        output.WindowsMediaType =
            preflight.MediaType ?? output.WindowsMediaType;
        output.DiskSizeBytes = preflight.DiskSizeBytes;
        output.IsSystemDisk = preflight.IsSystem;
        output.IsBootDisk = preflight.IsBoot;
        output.IsOffline = preflight.IsOffline;
        output.IsReadOnly = preflight.IsReadOnly;

        if (preflight.State != HddPreflightState.Pass)
        {
            output.State =
                preflight.State == HddPreflightState.Blocked
                    ? UnifiedStoragePreflightState.Blocked
                    : UnifiedStoragePreflightState.Error;
            output.Reason =
                "HDD preflight: " + (preflight.Reason ?? "unknown");
            output.Scope = preflight.Scope;
            return output;
        }

        output.State = UnifiedStoragePreflightState.Pass;
        output.Scope =
            "HDD preflight PASS. Target maps to " +
            output.PhysicalDrive +
            ". No destructive operation was executed.";
        output.Reason =
            "HDD logical-clear safety gates passed. Final execution must revalidate disk identity.";
        return output;
    }

    private static UnifiedStoragePreflightResult ApplyUsbGate(
        UnifiedStoragePreflightResult output,
        string targetPath,
        int diskNumber)
    {
        string root = NormalizeRoot(targetPath);
        UsbTargetPreflightResult preflight =
            UsbTargetPreflight.AnalyzeDrive(root);

        if (preflight == null)
        {
            output.State = UnifiedStoragePreflightState.Error;
            output.Reason = "USB preflight returned no result.";
            return output;
        }

        output.Model = preflight.Model ?? output.Model;
        output.SerialNumber = preflight.SerialNumber ?? output.SerialNumber;
        output.BusType = preflight.BusType ?? output.BusType;
        output.WindowsMediaType =
            preflight.MediaType ?? output.WindowsMediaType;
        output.DiskSizeBytes = preflight.DiskSizeBytes;
        output.PhysicalDrive = preflight.PhysicalDrive ?? output.PhysicalDrive;
        output.DiskNumber = preflight.DiskNumber;
        output.IsSystemDisk = preflight.IsSystem;
        output.IsBootDisk = preflight.IsBoot;
        output.IsOffline = preflight.IsOffline;
        output.IsReadOnly = preflight.IsReadOnly;

        if (preflight.DiskNumber != diskNumber)
        {
            output.State = UnifiedStoragePreflightState.Blocked;
            output.Reason =
                "USB target mapping changed during preflight.";
            return output;
        }

        if (preflight.State != UsbTargetPreflightState.Pass)
        {
            output.State =
                preflight.State == UsbTargetPreflightState.Blocked
                    ? UnifiedStoragePreflightState.Blocked
                    : UnifiedStoragePreflightState.Error;
            output.Reason =
                "USB preflight: " + (preflight.Reason ?? "unknown");
            output.Scope = preflight.Scope;
            return output;
        }

        output.State = UnifiedStoragePreflightState.Pass;
        output.Scope = preflight.Scope;
        output.Reason =
            "USB identity and safety gates passed. Final USB execution must revalidate identity.";
        return output;
    }

    private static UnifiedStoragePreflightResult ApplyNvmeGate(
        UnifiedStoragePreflightResult output,
        int diskNumber)
    {
        SanitizationPlan plan =
            StorageSanitizationProtocol.PrepareDeviceSanitizePreflight(
                diskNumber);

        CopyPlan(output, plan);

        if (plan.SafetyState == SanitizationSafetyState.Blocked)
        {
            output.State = UnifiedStoragePreflightState.Blocked;
            output.Reason = plan.SafetyBlockReason ?? plan.Reason;
            return output;
        }

        if (plan.DeviceSanitizeCapability !=
            DeviceSanitizeCapability.Supported)
        {
            output.State = UnifiedStoragePreflightState.Blocked;
            output.Reason =
                plan.SafetyBlockReason ??
                "NVMe sanitize capability is not supported or could not be determined.";
            return output;
        }

        output.State = UnifiedStoragePreflightState.Pass;
        output.CapabilityDetail = plan.NvmeCapabilityDetail;
        output.Scope =
            "NVMe read-only capability preflight PASS. No sanitize command was executed.";
        output.Reason =
            "NVMe SANICAP reports a supported device-level sanitize capability.";
        return output;
    }

    private static void CopyPlan(
        UnifiedStoragePreflightResult output,
        SanitizationPlan plan)
    {
        output.TargetPath = plan.DriveRoot ?? output.TargetPath;
        output.PhysicalDrive = plan.PhysicalDrive ?? "";
        output.DiskNumber = ParseDiskNumber(plan.DiskNumber);
        output.MediaKind = plan.MediaKind;
        output.Model = plan.Model ?? "";
        output.SerialNumber = plan.SerialNumber ?? "";
        output.BusType = plan.BusType ?? "";
        output.WindowsMediaType = plan.WindowsMediaType ?? "";
        output.Encrypted = plan.Encrypted;
        output.EncryptionStatus = plan.EncryptionStatus ?? "";
        output.IsSystemDisk = plan.IsSystemDisk;
        output.RecommendedStrength = plan.RecommendedStrength;
        output.RecommendedMethod = plan.RecommendedMethod ?? "";
        output.DeviceCommandRequired = plan.DeviceCommandRequired;
        output.DeviceSanitizeCapability = plan.DeviceSanitizeCapability;
        output.CapabilityDetail = plan.NvmeCapabilityDetail ?? "";
    }

    private static UnifiedStoragePreflightResult NewResult(string targetPath)
    {
        return new UnifiedStoragePreflightResult
        {
            State = UnifiedStoragePreflightState.Error,
            TargetPath = targetPath ?? "",
            DiskNumber = -1,
            MediaKind = StorageMediaKind.Unknown,
            DeviceSanitizeCapability =
                DeviceSanitizeCapability.NotEvaluated,
            Scope = "No write scope established."
        };
    }

    private static int ParseDiskNumber(string value)
    {
        int n;
        return int.TryParse(value, out n) ? n : -1;
    }

    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            string full = System.IO.Path.GetFullPath(path);
            string root = System.IO.Path.GetPathRoot(full);
            return string.IsNullOrWhiteSpace(root) ? "" : root;
        }
        catch
        {
            return "";
        }
    }
}

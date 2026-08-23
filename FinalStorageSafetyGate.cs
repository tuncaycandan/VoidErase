using System;
using System.IO;
using System.Management;
using VoidErase;

// This gate is intentionally read-only. It does not open a physical device for
// writing and it does not issue erase, overwrite, format, TRIM or sanitize calls.
// The system/boot-disk block is a code invariant and is not controlled by settings.
internal enum FinalStorageSafetyDecision
{
    Blocked,
    DryRunOnly,
    Error
}

internal sealed class FinalStorageSafetyResult
{
    internal FinalStorageSafetyDecision Decision { get; set; }
    internal string TargetPath { get; set; }
    internal string DriveRoot { get; set; }
    internal string PhysicalDrive { get; set; }
    internal int DiskNumber { get; set; }
    internal string Model { get; set; }
    internal string SerialNumber { get; set; }
    internal long SizeBytes { get; set; }
    internal bool IsSystemDisk { get; set; }
    internal bool IsBootDisk { get; set; }
    internal bool IsOffline { get; set; }
    internal bool IsReadOnly { get; set; }
    internal string Reason { get; set; }
    internal string Scope { get; set; }
}

internal static class FinalStorageSafetyGate
{
    // Deliberately no settings switch exists for these invariants.
    private const bool AlwaysBlockSystemDisk = true;
    private const bool AlwaysBlockBootDisk = true;

    internal static FinalStorageSafetyResult VerifyTarget(string targetPath)
    {
        FinalStorageSafetyResult result = new FinalStorageSafetyResult
        {
            Decision = FinalStorageSafetyDecision.Error,
            TargetPath = targetPath ?? "",
            DiskNumber = -1,
            Scope = "No write scope established."
        };

        try
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return Block(result, "Target path is empty.");

            string value = targetPath.Trim().Trim('"');
            string root = GetDriveRoot(value);
            result.DriveRoot = root;

            if (VoidEraseSafety.IsSameAsExecutable(value))
                return Block(result, "The VoidErase executable is always protected.");

            if (!string.IsNullOrWhiteSpace(root) &&
                VoidEraseSafety.IsProtectedPath(root))
            {
                return Block(result, "The target is inside a protected Windows or Program Files path.");
            }

            SanitizationPlan plan = StorageSanitizationProtocol.AnalyzePath(value);
            result.PhysicalDrive = plan.PhysicalDrive ?? "";
            result.Model = plan.Model ?? "";
            result.SerialNumber = plan.SerialNumber ?? "";
            result.IsSystemDisk = plan.IsSystemDisk;

            int number;
            if (!TryParsePhysicalDrive(plan.PhysicalDrive, out number))
                return Block(result, "The target could not be mapped to a physical disk.");

            result.DiskNumber = number;

            bool isBoot;
            bool isOffline;
            bool isReadOnly;
            long size;
            if (!TryReadMsftDiskFlags(number, out isBoot, out isOffline, out isReadOnly, out size))
                return Block(result, "Windows Storage safety flags could not be verified.");

            result.IsBootDisk = isBoot;
            result.IsOffline = isOffline;
            result.IsReadOnly = isReadOnly;
            result.SizeBytes = size;

            if (AlwaysBlockSystemDisk && result.IsSystemDisk)
                return Block(result, "The target physical disk is the Windows system disk.");

            if (AlwaysBlockBootDisk && result.IsBootDisk)
                return Block(result, "The target physical disk is a boot disk.");

            if (result.IsOffline)
                return Block(result, "The target physical disk is offline.");

            if (result.IsReadOnly)
                return Block(result, "The target physical disk is read-only.");

            if (result.SizeBytes <= 0)
                return Block(result, "The target physical disk reported an invalid size.");

            result.Decision = FinalStorageSafetyDecision.DryRunOnly;
            result.Scope =
                "DRY-RUN SAFETY RESULT: " + value + " -> " +
                result.PhysicalDrive + "; system/boot/offline/read-only checks passed. " +
                "No device write operation is authorized by this gate.";
            result.Reason = "Target passed read-only safety checks and remains dry-run only.";
            return result;
        }
        catch (Exception ex)
        {
            result.Decision = FinalStorageSafetyDecision.Error;
            result.Reason = "Safety gate failed closed: " + ex.Message;
            return result;
        }
    }

    private static FinalStorageSafetyResult Block(
        FinalStorageSafetyResult result,
        string reason)
    {
        result.Decision = FinalStorageSafetyDecision.Blocked;
        result.Scope = "No write scope established.";
        result.Reason = reason;
        return result;
    }

    private static string GetDriveRoot(string path)
    {
        try
        {
            if (path.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
                return "";

            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return root ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool TryParsePhysicalDrive(string value, out int number)
    {
        number = -1;
        if (string.IsNullOrWhiteSpace(value)) return false;
        const string prefix = @"\\.\PHYSICALDRIVE";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(value.Substring(prefix.Length), out number) && number >= 0;
    }

    private static bool TryReadMsftDiskFlags(
        int diskNumber,
        out bool isBoot,
        out bool isOffline,
        out bool isReadOnly,
        out long size)
    {
        isBoot = false;
        isOffline = false;
        isReadOnly = false;
        size = 0;

        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT Number, IsBoot, IsSystem, IsOffline, IsReadOnly, Size FROM MSFT_Disk"))
            using (ManagementObjectCollection disks = searcher.Get())
            {
                foreach (ManagementObject disk in disks)
                {
                    int current;
                    if (!TryGetInt32(disk["Number"], out current) || current != diskNumber)
                        continue;

                    bool isSystem;
                    if (!TryGetBoolean(disk["IsBoot"], out isBoot) ||
                        !TryGetBoolean(disk["IsSystem"], out isSystem) ||
                        !TryGetBoolean(disk["IsOffline"], out isOffline) ||
                        !TryGetBoolean(disk["IsReadOnly"], out isReadOnly))
                        return false;

                    if (isSystem)
                        isBoot = true;

                    size = ToInt64(disk["Size"]);
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryGetBoolean(object value, out bool result)
    {
        result = false;
        if (value == null) return false;
        if (value is bool)
        {
            result = (bool)value;
            return true;
        }
        return bool.TryParse(Convert.ToString(value), out result);
    }

    private static bool TryGetInt32(object value, out int result)
    {
        result = 0;
        return value != null && int.TryParse(Convert.ToString(value), out result);
    }

    private static long ToInt64(object value)
    {
        long result;
        return long.TryParse(Convert.ToString(value), out result) ? result : 0L;
    }
}

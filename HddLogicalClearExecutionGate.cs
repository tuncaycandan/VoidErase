using System;
using System.Collections.Generic;
using System.Management;
using System.IO;

internal enum HddExecutionGateState
{
    Pass,
    Blocked,
    Error
}

internal sealed class HddExecutionGateResult
{
    internal HddExecutionGateState State { get; set; }
    internal string TargetPath { get; set; }
    internal string PhysicalDrive { get; set; }
    internal int DiskNumber { get; set; }
    internal string Model { get; set; }
    internal string SerialNumber { get; set; }
    internal string ExpectedModel { get; set; }
    internal string ExpectedSerialNumber { get; set; }
    internal long DiskSizeBytes { get; set; }
    internal uint LogicalSectorSize { get; set; }
    internal uint PhysicalSectorSize { get; set; }
    internal bool IsSystem { get; set; }
    internal bool IsBoot { get; set; }
    internal bool IsOffline { get; set; }
    internal bool IsReadOnly { get; set; }
    internal string MediaType { get; set; }
    internal string Reason { get; set; }
    internal string Scope { get; set; }
}

internal static class HddLogicalClearExecutionGate
{
    // FINAL PRE-EXECUTION GATE — DRY-RUN ONLY.
    //
    // This class does NOT open a physical disk for writing and does NOT issue
    // erase, overwrite, TRIM, sanitize, format or destructive IOCTL commands.
    //
    // It only verifies that the target path still maps to the expected
    // physical disk and that the disk identity/safety properties have not
    // changed since the operator selected the target.

    internal static HddExecutionGateResult Verify(
        string targetPath,
        int expectedDiskNumber,
        string expectedModel,
        string expectedSerialNumber,
        long expectedMinimumSizeBytes)
    {
        HddExecutionGateResult result = new HddExecutionGateResult
        {
            State = HddExecutionGateState.Error,
            TargetPath = targetPath ?? "",
            DiskNumber = expectedDiskNumber,
            PhysicalDrive = @"\\.\PHYSICALDRIVE" + expectedDiskNumber,
            ExpectedModel = expectedModel ?? "",
            ExpectedSerialNumber = expectedSerialNumber ?? "",
            Scope = "No write scope established."
        };

        try
        {
            FinalStorageSafetyResult finalSafety =
                FinalStorageSafetyGate.VerifyTarget(targetPath);

            if (finalSafety.Decision != FinalStorageSafetyDecision.DryRunOnly)
            {
                result.State = finalSafety.Decision == FinalStorageSafetyDecision.Blocked
                    ? HddExecutionGateState.Blocked
                    : HddExecutionGateState.Error;
                result.IsSystem = finalSafety.IsSystemDisk;
                result.IsBoot = finalSafety.IsBootDisk;
                result.IsOffline = finalSafety.IsOffline;
                result.IsReadOnly = finalSafety.IsReadOnly;
                result.PhysicalDrive = finalSafety.PhysicalDrive ?? result.PhysicalDrive;
                result.DiskNumber = finalSafety.DiskNumber;
                result.Model = finalSafety.Model ?? "";
                result.SerialNumber = finalSafety.SerialNumber ?? "";
                result.DiskSizeBytes = finalSafety.SizeBytes;
                result.Reason = "Final storage safety gate: " +
                    (finalSafety.Reason ?? "target blocked");
                return result;
            }

            string root = NormalizeDriveRoot(targetPath);

            if (string.IsNullOrEmpty(root))
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason = "Target path is not a valid local drive path.";
                return result;
            }

            // ------------------------------------------------------------
            // 1) Map target drive letter -> partition -> physical disk.
            // ------------------------------------------------------------
            int mappedDiskNumber;
            if (!TryResolveDriveToDiskNumber(root, out mappedDiskNumber))
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "The target drive could not be mapped to a physical disk.";
                return result;
            }

            if (mappedDiskNumber != expectedDiskNumber)
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "Target mapping changed: " + root +
                    " currently maps to PHYSICALDRIVE" +
                    mappedDiskNumber +
                    ", but the expected disk is PHYSICALDRIVE" +
                    expectedDiskNumber + ".";
                return result;
            }

            // ------------------------------------------------------------
            // 2) Re-read physical identity from Win32_DiskDrive.
            // ------------------------------------------------------------
            string detectedModel;
            string detectedSerialNumber;
            long detectedDiskSizeBytes;
            string detectedMediaType;

            if (!TryGetPhysicalDisk(
                expectedDiskNumber,
                out detectedModel,
                out detectedSerialNumber,
                out detectedDiskSizeBytes,
                out detectedMediaType))
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "The expected physical disk could not be re-identified.";
                return result;
            }

            result.Model = detectedModel;
            result.SerialNumber = detectedSerialNumber;
            result.DiskSizeBytes = detectedDiskSizeBytes;
            result.MediaType = detectedMediaType;

            if (!string.Equals(
                Normalize(result.Model),
                Normalize(expectedModel),
                StringComparison.OrdinalIgnoreCase))
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "Disk model changed or does not match the operator-approved identity.";
                return result;
            }

            if (!string.Equals(
                Normalize(result.SerialNumber),
                Normalize(expectedSerialNumber),
                StringComparison.OrdinalIgnoreCase))
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "Disk serial number changed or does not match the operator-approved identity.";
                return result;
            }

            if (result.DiskSizeBytes < expectedMinimumSizeBytes)
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "The current disk is smaller than the operator-approved minimum size.";
                return result;
            }

            if (!IsHdd(result.MediaType))
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "The target is not positively classified as an HDD.";
                return result;
            }

            // ------------------------------------------------------------
            // 3) Best-effort Windows safety flags.
            //    Failure to obtain these flags is conservative: BLOCKED.
            // ------------------------------------------------------------
            uint logicalSectorSize;
            uint physicalSectorSize;

            if (!TryGetMsftDiskSectorGeometry(
                expectedDiskNumber,
                out logicalSectorSize,
                out physicalSectorSize))
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "Disk sector geometry could not be verified.";
                return result;
            }

            result.LogicalSectorSize = logicalSectorSize;
            result.PhysicalSectorSize = physicalSectorSize;

            bool isSystem;
            bool isBoot;
            bool isOffline;
            bool isReadOnly;

            if (!TryGetMsftDiskSafetyFlags(
                expectedDiskNumber,
                out isSystem,
                out isBoot,
                out isOffline,
                out isReadOnly))
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "Windows Storage safety flags could not be verified.";
                return result;
            }

            result.IsSystem = isSystem;
            result.IsBoot = isBoot;
            result.IsOffline = isOffline;
            result.IsReadOnly = isReadOnly;

            if (result.IsSystem)
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "The target physical disk is reported as the system disk.";
                return result;
            }

            if (result.IsBoot)
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "The target physical disk is reported as a boot disk.";
                return result;
            }

            if (result.IsOffline)
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "The target physical disk is offline.";
                return result;
            }

            if (result.IsReadOnly)
            {
                result.State = HddExecutionGateState.Blocked;
                result.Reason =
                    "The target physical disk is read-only.";
                return result;
            }

            result.State = HddExecutionGateState.Pass;
            result.Scope =
                "DRY-RUN FINAL GATE: " + root +
                " -> PHYSICALDRIVE" + expectedDiskNumber +
                "; model/serial/size/media and Windows safety flags revalidated. " +
                "No destructive command was executed.";
            result.Reason =
                "The target still matches the operator-approved HDD identity " +
                "and all final safety gates passed.";

            return result;
        }
        catch (Exception ex)
        {
            result.State = HddExecutionGateState.Error;
            result.Reason = "Execution gate failed: " + ex.Message;
            return result;
        }
    }

    private static string NormalizeDriveRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            string full = Path.GetFullPath(path.Trim());

            if (full.Length < 2 || full[1] != ':')
                return "";

            string root = full.Substring(0, 2).ToUpperInvariant() + "\\";

            if (!Directory.Exists(root))
                return "";

            return root;
        }
        catch
        {
            return "";
        }
    }

    private static bool TryResolveDriveToDiskNumber(
        string driveRoot,
        out int diskNumber)
    {
        diskNumber = -1;

        string drive = driveRoot.Substring(0, 2);

        try
        {
            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT Antecedent,Dependent FROM Win32_LogicalDiskToPartition"))
            using (ManagementObjectCollection links = searcher.Get())
            {
                foreach (ManagementObject link in links)
                {
                    string dependent = Convert.ToString(link["Dependent"]) ?? "";
                    string antecedent = Convert.ToString(link["Antecedent"]) ?? "";

                    // Example dependent:
                    // Win32_LogicalDisk.DeviceID="D:"
                    if (dependent.IndexOf(
                        "DeviceID=\"" + drive + "\"",
                        StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    int partitionDisk;
                    if (!TryParseDiskNumberFromPartition(
                        antecedent, out partitionDisk))
                        continue;

                    diskNumber = partitionDisk;
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

    private static bool TryParseDiskNumberFromPartition(
        string antecedent,
        out int diskNumber)
    {
        diskNumber = -1;

        // Typical form:
        // Win32_DiskPartition.DeviceID="Disk #0, Partition #1"
        const string marker = "Disk #";

        int start = antecedent.IndexOf(
            marker, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
            return false;

        start += marker.Length;

        int comma = antecedent.IndexOf(',', start);
        if (comma < 0)
            return false;

        string number = antecedent.Substring(start, comma - start).Trim();
        return int.TryParse(number, out diskNumber);
    }

    private static bool TryGetPhysicalDisk(
        int diskNumber,
        out string model,
        out string serial,
        out long size,
        out string mediaType)
    {
        model = "";
        serial = "";
        size = 0;
        mediaType = "";

        try
        {
            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT DeviceID,Model,SerialNumber,Size,MediaType FROM Win32_DiskDrive"))
            using (ManagementObjectCollection disks = searcher.Get())
            {
                foreach (ManagementObject disk in disks)
                {
                    string deviceId = Convert.ToString(disk["DeviceID"]) ?? "";

                    int number;
                    if (!TryParsePhysicalDriveNumber(deviceId, out number))
                        continue;

                    if (number != diskNumber)
                        continue;

                    model = Convert.ToString(disk["Model"]) ?? "";
                    serial = Normalize(Convert.ToString(disk["SerialNumber"]));
                    size = ToInt64(disk["Size"]);
                    mediaType = Convert.ToString(disk["MediaType"]) ?? "";
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

    private static bool TryGetMsftDiskSectorGeometry(
        int diskNumber,
        out uint logicalSectorSize,
        out uint physicalSectorSize)
    {
        logicalSectorSize = 0;
        physicalSectorSize = 0;

        try
        {
            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT * FROM MSFT_Disk"))
            using (ManagementObjectCollection disks = searcher.Get())
            {
                foreach (ManagementObject disk in disks)
                {
                    int number;
                    if (!TryGetInt32(disk["Number"], out number))
                        continue;

                    if (number != diskNumber)
                        continue;

                    logicalSectorSize =
                        ToUInt32(disk["LogicalSectorSize"]);
                    physicalSectorSize =
                        ToUInt32(disk["PhysicalSectorSize"]);

                    return logicalSectorSize > 0 &&
                           physicalSectorSize > 0;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryGetMsftDiskSafetyFlags(
        int diskNumber,
        out bool isSystem,
        out bool isBoot,
        out bool isOffline,
        out bool isReadOnly)
    {
        isSystem = false;
        isBoot = false;
        isOffline = false;
        isReadOnly = false;

        bool found = false;

        try
        {
            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT * FROM MSFT_Disk"))
            using (ManagementObjectCollection disks = searcher.Get())
            {
                foreach (ManagementObject disk in disks)
                {
                    int number;
                    if (!TryGetInt32(disk["Number"], out number))
                        continue;

                    if (number != diskNumber)
                        continue;

                    bool systemValue;
                    bool bootValue;
                    bool offlineValue;
                    bool readOnlyValue;

                    if (!TryGetBoolProperty(
                        disk, "IsSystem", out systemValue))
                        return false;

                    if (!TryGetBoolProperty(
                        disk, "BootFromDisk", out bootValue))
                        return false;

                    if (!TryGetBoolProperty(
                        disk, "IsOffline", out offlineValue))
                        return false;

                    if (!TryGetBoolProperty(
                        disk, "IsReadOnly", out readOnlyValue))
                        return false;

                    isSystem = systemValue;
                    isBoot = bootValue;
                    isOffline = offlineValue;
                    isReadOnly = readOnlyValue;
                    found = true;
                    break;
                }
            }
        }
        catch
        {
            return false;
        }

        return found;
    }

    private static bool IsHdd(string mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            return false;

        return mediaType.IndexOf(
            "HDD", StringComparison.OrdinalIgnoreCase) >= 0
            || mediaType.IndexOf(
            "Hard Disk", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryParsePhysicalDriveNumber(
        string deviceId, out int diskNumber)
    {
        diskNumber = -1;

        const string prefix = @"\\.\PHYSICALDRIVE";

        if (string.IsNullOrWhiteSpace(deviceId) ||
            !deviceId.StartsWith(
                prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(
            deviceId.Substring(prefix.Length),
            out diskNumber);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim();
    }

    private static bool TryGetBoolProperty(
        ManagementObject obj, string property, out bool value)
    {
        value = false;

        try
        {
            object raw = obj[property];

            if (raw == null)
                return false;

            value = Convert.ToBoolean(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetInt32(object value, out int result)
    {
        result = 0;

        if (value == null)
            return false;

        try
        {
            result = Convert.ToInt32(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static uint ToUInt32(object value)
    {
        if (value == null)
            return 0;

        try
        {
            return Convert.ToUInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private static long ToInt64(object value)
    {
        if (value == null)
            return 0;

        try
        {
            return Convert.ToInt64(value);
        }
        catch
        {
            return 0;
        }
    }
}

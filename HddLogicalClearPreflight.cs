using System;
using System.Management;

internal enum HddPreflightState
{
    Pass,
    Blocked,
    Error
}

internal sealed class HddLogicalClearPreflightResult
{
    internal HddPreflightState State { get; set; }
    internal int DiskNumber { get; set; }
    internal string PhysicalDrive { get; set; }
    internal string Model { get; set; }
    internal string SerialNumber { get; set; }
    internal string BusType { get; set; }
    internal string MediaType { get; set; }
    internal long DiskSizeBytes { get; set; }
    internal uint LogicalSectorSize { get; set; }
    internal uint PhysicalSectorSize { get; set; }
    internal bool IsSystem { get; set; }
    internal bool IsBoot { get; set; }
    internal bool IsOffline { get; set; }
    internal bool IsReadOnly { get; set; }
    internal string Reason { get; set; }
    internal string Scope { get; set; }
}

internal static class HddLogicalClearPreflight
{
    // DRY-RUN ONLY. No disk is opened for writing and no destructive
    // command, IOCTL, format, TRIM, sanitize or overwrite is issued.
    internal static HddLogicalClearPreflightResult AnalyzeDisk(int diskNumber)
    {
        if (diskNumber < 0)
            throw new ArgumentOutOfRangeException("diskNumber");

        HddLogicalClearPreflightResult result =
            new HddLogicalClearPreflightResult
            {
                State = HddPreflightState.Error,
                DiskNumber = diskNumber,
                PhysicalDrive = @"\\.\PHYSICALDRIVE" + diskNumber,
                Scope = "No write scope established."
            };

        try
        {
            // Do not put the disk number into a WMI WHERE clause.
            // Some Windows builds/providers reject that query with
            // "Invalid query". Read the class and match Number in managed code.
            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT * FROM MSFT_Disk"))
            using (ManagementObjectCollection disks = searcher.Get())
            {
                ManagementObject found = null;

                foreach (ManagementObject disk in disks)
                {
                    int number;
                    if (!TryGetInt32(disk["Number"], out number))
                        continue;

                    if (number == diskNumber)
                    {
                        found = disk;
                        break;
                    }
                }

                if (found == null)
                {
                    result.Reason =
                        "The requested physical disk was not found in MSFT_Disk.";
                    return result;
                }

                result.Model = Convert.ToString(found["FriendlyName"]) ?? "";
                result.SerialNumber = Convert.ToString(found["SerialNumber"]) ?? "";
                result.BusType = Convert.ToString(found["BusType"]) ?? "";
                result.MediaType = Convert.ToString(found["MediaType"]) ?? "";
                result.DiskSizeBytes = ToInt64(found["Size"]);
                result.LogicalSectorSize =
                    ToUInt32(found["LogicalSectorSize"]);
                result.PhysicalSectorSize =
                    ToUInt32(found["PhysicalSectorSize"]);
                result.IsSystem = ToBool(found["IsSystem"]);
                result.IsBoot = ToBool(found["BootFromDisk"]);
                result.IsOffline = ToBool(found["IsOffline"]);
                result.IsReadOnly = ToBool(found["IsReadOnly"]);
            }

            if (result.IsSystem)
            {
                result.State = HddPreflightState.Blocked;
                result.Reason =
                    "The physical disk contains the running Windows system volume.";
                return result;
            }

            if (result.IsBoot)
            {
                result.State = HddPreflightState.Blocked;
                result.Reason =
                    "Windows reports this physical disk as a boot disk.";
                return result;
            }

            if (result.IsOffline)
            {
                result.State = HddPreflightState.Blocked;
                result.Reason = "The physical disk is offline.";
                return result;
            }

            if (result.IsReadOnly)
            {
                result.State = HddPreflightState.Blocked;
                result.Reason = "The physical disk is read-only.";
                return result;
            }

            if (!string.Equals(result.MediaType, "HDD",
                StringComparison.OrdinalIgnoreCase))
            {
                result.State = HddPreflightState.Blocked;
                result.Reason =
                    "LogicalClear is restricted to media classified as HDD.";
                return result;
            }

            if (result.DiskSizeBytes <= 0)
            {
                result.State = HddPreflightState.Blocked;
                result.Reason =
                    "The physical disk size could not be established safely.";
                return result;
            }

            if (result.LogicalSectorSize == 0)
            {
                result.State = HddPreflightState.Blocked;
                result.Reason =
                    "The logical sector size could not be established safely.";
                return result;
            }

            result.State = HddPreflightState.Pass;
            result.Scope =
                "DRY-RUN SCOPE: full reported addressable disk size = " +
                result.DiskSizeBytes.ToString("N0") +
                " bytes; no sectors will be written by this preflight.";
            result.Reason =
                "Non-system HDD identified; disk geometry and safety gates " +
                "are available for a separate, explicitly approved execution phase.";

            return result;
        }
        catch (Exception ex)
        {
            result.State = HddPreflightState.Error;
            result.Reason = "Preflight failed: " + ex.Message;
            return result;
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

    private static bool ToBool(object value)
    {
        if (value == null)
            return false;

        try { return Convert.ToBoolean(value); }
        catch { return false; }
    }

    private static long ToInt64(object value)
    {
        if (value == null)
            return 0;

        try { return Convert.ToInt64(value); }
        catch { return 0; }
    }

    private static uint ToUInt32(object value)
    {
        if (value == null)
            return 0;

        try { return Convert.ToUInt32(value); }
        catch { return 0; }
    }
}

using System;
using System.Management;
using System.IO;

internal enum UsbTargetPreflightState
{
    Pass,
    Blocked,
    Error
}

internal sealed class UsbTargetPreflightResult
{
    internal UsbTargetPreflightState State { get; set; }
    internal string DriveRoot { get; set; }
    internal string PhysicalDrive { get; set; }
    internal int DiskNumber { get; set; }
    internal string Model { get; set; }
    internal string SerialNumber { get; set; }
    internal string BusType { get; set; }
    internal string MediaType { get; set; }
    internal long DiskSizeBytes { get; set; }
    internal bool IsSystem { get; set; }
    internal bool IsBoot { get; set; }
    internal bool IsOffline { get; set; }
    internal bool IsReadOnly { get; set; }
    internal string Reason { get; set; }
    internal string Scope { get; set; }
}

internal static class UsbTargetPreflight
{
    // DRY-RUN ONLY.
    // No disk is opened for writing and no erase, overwrite, sanitize,
    // format, TRIM or destructive IOCTL is issued.
    internal static UsbTargetPreflightResult AnalyzeDrive(string driveRoot)
    {
        UsbTargetPreflightResult result = new UsbTargetPreflightResult
        {
            State = UsbTargetPreflightState.Error,
            DriveRoot = NormalizeRoot(driveRoot),
            DiskNumber = -1,
            Scope = "No write scope established."
        };

        try
        {
            if (string.IsNullOrWhiteSpace(result.DriveRoot))
            {
                result.State = UsbTargetPreflightState.Blocked;
                result.Reason = "Target is not a valid local drive root.";
                return result;
            }

            int diskNumber;
            if (!TryResolveDriveNumber(result.DriveRoot, out diskNumber))
            {
                result.State = UsbTargetPreflightState.Blocked;
                result.Reason =
                    "The selected drive could not be mapped to a physical disk.";
                return result;
            }

            result.DiskNumber = diskNumber;
            result.PhysicalDrive = @"\\.\PHYSICALDRIVE" + diskNumber;

            // Do NOT depend on MSFT_Disk for the primary identity/size lookup.
            // Win32_DiskDrive reliably reports the physical USB device on this
            // Windows installation and avoids the previous "Not found" failure.
            string model;
            string serialNumber;
            string busType;
            string mediaType;
            long diskSizeBytes;

            if (!TryReadPhysicalDisk(
                result.PhysicalDrive,
                out model,
                out serialNumber,
                out busType,
                out mediaType,
                out diskSizeBytes))
            {
                result.State = UsbTargetPreflightState.Error;
                result.Reason =
                    "The mapped physical USB disk could not be read from Win32_DiskDrive.";
                return result;
            }

            result.Model = model;
            result.SerialNumber = serialNumber;
            result.BusType = busType;
            result.MediaType = mediaType;
            result.DiskSizeBytes = diskSizeBytes;

            // The selected volume itself must be on a USB physical disk.
            if (!string.Equals(
                result.BusType,
                "USB",
                StringComparison.OrdinalIgnoreCase))
            {
                result.State = UsbTargetPreflightState.Blocked;
                result.Reason =
                    "The selected drive is not reported by Windows as a USB disk.";
                return result;
            }

            // E:\ must never be treated as safe merely because it is removable.
            // Explicitly reject the running Windows volume.
            result.IsSystem = IsWindowsSystemDrive(result.DriveRoot);

            if (result.IsSystem)
            {
                result.State = UsbTargetPreflightState.Blocked;
                result.Reason =
                    "The selected USB drive is the running Windows system volume.";
                return result;
            }

            // Read the disk-level safety flags when MSFT_Disk is available.
            // These are optional enrichment fields; a missing Storage-provider
            // property must NOT turn a valid Win32_DiskDrive result into "Not found".
            bool isBoot;
            bool isOffline;
            bool isReadOnly;

            ReadOptionalMsftDiskSafety(
                diskNumber,
                out isBoot,
                out isOffline,
                out isReadOnly);

            result.IsBoot = isBoot;
            result.IsOffline = isOffline;
            result.IsReadOnly = isReadOnly;

            if (result.IsBoot)
            {
                result.State = UsbTargetPreflightState.Blocked;
                result.Reason =
                    "Windows reports the selected physical disk as a boot disk.";
                return result;
            }

            if (result.IsOffline)
            {
                result.State = UsbTargetPreflightState.Blocked;
                result.Reason = "The selected USB disk is offline.";
                return result;
            }

            if (result.IsReadOnly)
            {
                result.State = UsbTargetPreflightState.Blocked;
                result.Reason = "The selected USB disk is read-only.";
                return result;
            }

            if (result.DiskSizeBytes <= 0)
            {
                result.State = UsbTargetPreflightState.Error;
                result.Reason =
                    "The USB physical disk reported an invalid size.";
                return result;
            }

            result.State = UsbTargetPreflightState.Pass;
            result.Scope =
                "DRY-RUN TARGET SCOPE: " +
                result.DriveRoot +
                " maps to " +
                result.PhysicalDrive +
                "; physical disk identity and safety gates passed; " +
                "no sectors will be written by this preflight.";

            result.Reason =
                "USB target identified successfully from Win32_DiskDrive. " +
                "System/boot/offline/read-only safety gates passed. " +
                "No destructive operation was executed.";

            return result;
        }
        catch (Exception ex)
        {
            result.State = UsbTargetPreflightState.Error;
            result.Reason = "USB preflight failed: " + ex.Message;
            return result;
        }
    }

    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);

            if (string.IsNullOrWhiteSpace(root))
                return "";

            return root;
        }
        catch
        {
            return "";
        }
    }

    private static bool TryResolveDriveNumber(
        string driveRoot,
        out int diskNumber)
    {
        diskNumber = -1;

        // Prefer the stable Win32 association:
        // Win32_LogicalDisk.DeviceID -> Win32_LogicalDiskToPartition ->
        // Win32_DiskPartition -> DiskIndex.
        try
        {
            string letter =
                driveRoot.TrimEnd('\\').TrimEnd(':').ToUpperInvariant();

            string logicalDevice = letter + ":";

            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT DeviceID, Name FROM Win32_LogicalDisk"))
            using (ManagementObjectCollection logicalDisks = searcher.Get())
            {
                bool exists = false;

                foreach (ManagementObject logical in logicalDisks)
                {
                    string deviceId =
                        Convert.ToString(logical["DeviceID"]) ?? "";

                    if (string.Equals(
                        deviceId,
                        logicalDevice,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    return false;
            }

            string escapedDevice =
                logicalDevice.Replace("\\", "\\\\").Replace("'", "\\'");

            using (ManagementObjectSearcher associationSearcher =
                new ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT Antecedent, Dependent " +
                    "FROM Win32_LogicalDiskToPartition"))
            using (ManagementObjectCollection associations =
                associationSearcher.Get())
            {
                foreach (ManagementObject association in associations)
                {
                    string dependent =
                        Convert.ToString(association["Dependent"]) ?? "";

                    if (dependent.IndexOf(
                        "DeviceID=\"" + logicalDevice + "\"",
                        StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string antecedent =
                        Convert.ToString(association["Antecedent"]) ?? "";

                    int diskIndex = ParseDiskIndex(antecedent);

                    if (diskIndex >= 0)
                    {
                        diskNumber = diskIndex;
                        return true;
                    }
                }
            }

            // Fallback: query partitions directly and compare DriveLetter.
            using (ManagementObjectSearcher partitionSearcher =
                new ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT DiskIndex, DriveLetter FROM Win32_DiskPartition"))
            using (ManagementObjectCollection partitions =
                partitionSearcher.Get())
            {
                foreach (ManagementObject partition in partitions)
                {
                    string partitionLetter =
                        Convert.ToString(partition["DriveLetter"]) ?? "";

                    if (!string.Equals(
                        partitionLetter,
                        logicalDevice,
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (TryGetInt32(partition["DiskIndex"], out diskNumber))
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

    private static bool TryReadPhysicalDisk(
        string physicalDrive,
        out string model,
        out string serialNumber,
        out string busType,
        out string mediaType,
        out long diskSizeBytes)
    {
        model = "";
        serialNumber = "";
        busType = "";
        mediaType = "";
        diskSizeBytes = 0;

        try
        {
            string escaped =
                physicalDrive.Replace("\\", "\\\\").Replace("'", "\\'");

            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT DeviceID, Model, SerialNumber, Size, MediaType, " +
                    "InterfaceType, PNPDeviceID " +
                    "FROM Win32_DiskDrive WHERE DeviceID='" + escaped + "'"))
            using (ManagementObjectCollection disks = searcher.Get())
            {
                foreach (ManagementObject disk in disks)
                {
                    model = Convert.ToString(disk["Model"]) ?? "";
                    serialNumber =
                        (Convert.ToString(disk["SerialNumber"]) ?? "").Trim();

                    diskSizeBytes = ToInt64(disk["Size"]);
                    mediaType = Convert.ToString(disk["MediaType"]) ?? "";

                    string interfaceType =
                        Convert.ToString(disk["InterfaceType"]) ?? "";

                    string pnpDeviceId =
                        Convert.ToString(disk["PNPDeviceID"]) ?? "";

                    busType = NormalizeBusType(
                        interfaceType,
                        pnpDeviceId);

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

    private static void ReadOptionalMsftDiskSafety(
        int diskNumber,
        out bool isBoot,
        out bool isOffline,
        out bool isReadOnly)
    {
        isBoot = false;
        isOffline = false;
        isReadOnly = false;

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

                    // Each property is read independently. A missing provider
                    // property cannot abort the whole USB preflight.
                    isBoot = ReadBoolProperty(disk, "IsBoot");
                    if (!isBoot)
                        isBoot = ReadBoolProperty(disk, "BootFromDisk");

                    isOffline = ReadBoolProperty(disk, "IsOffline");
                    isReadOnly = ReadBoolProperty(disk, "IsReadOnly");

                    return;
                }
            }
        }
        catch
        {
            // Optional provider. The mandatory identity checks above remain valid.
        }
    }

    private static bool IsWindowsSystemDrive(string driveRoot)
    {
        try
        {
            string systemRoot = Path.GetPathRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows));

            if (string.IsNullOrWhiteSpace(systemRoot))
                return false;

            return string.Equals(
                NormalizeRoot(systemRoot),
                NormalizeRoot(driveRoot),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int ParseDiskIndex(string antecedent)
    {
        if (string.IsNullOrWhiteSpace(antecedent))
            return -1;

        int marker = antecedent.IndexOf(
            "Disk #",
            StringComparison.OrdinalIgnoreCase);

        if (marker < 0)
            return -1;

        int start = marker + 6;
        int end = start;

        while (end < antecedent.Length &&
               char.IsDigit(antecedent[end]))
        {
            end++;
        }

        if (end <= start)
            return -1;

        int value;
        if (int.TryParse(
            antecedent.Substring(start, end - start),
            out value))
            return value;

        return -1;
    }

    private static string NormalizeBusType(
        string interfaceType,
        string pnpDeviceId)
    {
        if (string.Equals(
            interfaceType,
            "USB",
            StringComparison.OrdinalIgnoreCase))
            return "USB";

        if (!string.IsNullOrWhiteSpace(pnpDeviceId) &&
            pnpDeviceId.IndexOf(
                "USBSTOR",
                StringComparison.OrdinalIgnoreCase) >= 0)
            return "USB";

        if (!string.IsNullOrWhiteSpace(pnpDeviceId) &&
            pnpDeviceId.IndexOf(
                "USB\\",
                StringComparison.OrdinalIgnoreCase) >= 0)
            return "USB";

        return interfaceType ?? "";
    }

    private static bool TryGetInt32(
        object value,
        out int result)
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

    private static bool ReadBoolProperty(
        ManagementObject disk,
        string propertyName)
    {
        try
        {
            return ToBool(disk[propertyName]);
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

        try
        {
            return Convert.ToBoolean(value);
        }
        catch
        {
            return false;
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

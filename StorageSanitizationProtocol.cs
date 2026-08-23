using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

internal enum StorageMediaKind
{
    Unknown,
    Hdd,
    SataSsd,
    Nvme,
    UsbFlash,
    Virtual,
    Other
}

internal enum SanitizationStrength
{
    LogicalClear,
    DevicePurge,
    CryptographicErase,
    Destroy,
    Blocked
}

// Device-level execution is deliberately separated from the decision engine.
// The protocol can say what would be appropriate, but it never issues a destructive
// device command from analysis/test paths. A later executor must satisfy every
// preflight gate before any device operation is even considered.
internal enum DeviceSanitizeCapability
{
    NotEvaluated,
    Supported,
    Unsupported,
    Unknown
}

internal enum SanitizationSafetyState
{
    Blocked,
    PreflightOnly,
    ReadyForSeparateExecutor
}

internal sealed class SanitizationPlan
{
    internal string DriveRoot { get; set; }
    internal string PhysicalDrive { get; set; }
    internal string DiskNumber { get; set; }
    internal StorageMediaKind MediaKind { get; set; }
    internal bool Encrypted { get; set; }
    internal string EncryptionStatus { get; set; }
    internal bool IsSystemDisk { get; set; }
    internal string Model { get; set; }
    internal string SerialNumber { get; set; }
    internal string BusType { get; set; }
    internal string WindowsMediaType { get; set; }
    internal SanitizationStrength RecommendedStrength { get; set; }
    internal string RecommendedMethod { get; set; }
    internal string Reason { get; set; }
    internal bool DeviceCommandRequired { get; set; }

    // Execution metadata. These values are advisory/preflight only; this file
    // does not execute erase, sanitize, secure-erase, or firmware commands.
    internal DeviceSanitizeCapability DeviceSanitizeCapability { get; set; }
    internal uint NvmeSanitizeCapabilitiesRaw { get; set; }
    internal bool NvmeCryptoEraseSupported { get; set; }
    internal bool NvmeBlockEraseSupported { get; set; }
    internal bool NvmeOverwriteSupported { get; set; }
    internal bool NvmeNdi { get; set; }
    internal bool NvmeNodmmasModifiesMedia { get; set; }
    internal string NvmeCapabilityDetail { get; set; }
    internal SanitizationSafetyState SafetyState { get; set; }
    internal string SafetyBlockReason { get; set; }
}

internal static class StorageSanitizationProtocol
{
    internal static SanitizationPlan AnalyzePath(string path)
    {
        // Physical-disk targets such as \\.\PHYSICALDRIVE2 do not have a
        // normal volume root. Route them directly through the physical-disk
        // analyzer instead of attempting Path.GetPathRoot().
        int physicalDiskNumber;
        if (TryParsePhysicalDrivePath(path, out physicalDiskNumber))
            return AnalyzePhysicalDiskNumber(physicalDiskNumber);

        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root))
            throw new IOException("Storage root could not be determined.");

        string drive = root.TrimEnd('\\');
        string physicalDrive = ResolvePhysicalDrive(drive);

        if (physicalDrive == null)
        {
            return new SanitizationPlan
            {
                DriveRoot = root,
                PhysicalDrive = "",
                DiskNumber = "",
                MediaKind = StorageMediaKind.Unknown,
                Encrypted = IsEncrypted(drive),
                EncryptionStatus = GetEncryptionStatus(drive),
                IsSystemDisk = IsSystemDrive(drive),
                Model = "",
                SerialNumber = "",
                BusType = "",
                WindowsMediaType = "",
                RecommendedStrength = SanitizationStrength.LogicalClear,
                RecommendedMethod = "BLOCKED: physical-disk mapping failed; no sanitization method selected",
                Reason = "The target volume could not be mapped safely to a physical disk.",
                DeviceCommandRequired = false
            };
        }

        Dictionary<string, string> info = QueryDisk(physicalDrive);
        string model = Get(info, "Model");
        string media = Get(info, "MediaType");
        string iface = Get(info, "InterfaceType");
        string bus = Get(info, "BusType");
        string pnp = Get(info, "PNPDeviceID");
        string serial = Get(info, "SerialNumber");
        string diskNumber = Get(info, "DiskNumber");
        string storageMediaType = Get(info, "StorageMediaType");

        StorageMediaKind kind = Classify(model, media, iface, bus, pnp, storageMediaType);
        bool encrypted = IsEncrypted(drive);
        string encryptionStatus = GetEncryptionStatus(drive);

        string systemRoot =
            Path.GetPathRoot(Environment.SystemDirectory) ?? Environment.SystemDirectory;
        string systemDrive = systemRoot.TrimEnd('\\');

        bool system = string.Equals(
            physicalDrive,
            ResolvePhysicalDrive(systemDrive),
            StringComparison.OrdinalIgnoreCase);

        SanitizationPlan plan = new SanitizationPlan
        {
            DriveRoot = root,
            PhysicalDrive = physicalDrive,
            DiskNumber = diskNumber,
            MediaKind = kind,
            Encrypted = encrypted,
            EncryptionStatus = encryptionStatus,
            IsSystemDisk = system,
            Model = model,
            SerialNumber = serial,
            BusType = ResolveBusType(bus, model, pnp, iface),
            WindowsMediaType = string.IsNullOrWhiteSpace(storageMediaType)
                ? media
                : storageMediaType
        };

        return ApplySanitizationDecision(plan);
    }

    private static bool TryParsePhysicalDrivePath(string path, out int diskNumber)
    {
        diskNumber = -1;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        string value = path.Trim().Trim('"');

        const string prefix = @"\\.\PHYSICALDRIVE";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string suffix = value.Substring(prefix.Length);
        return int.TryParse(suffix, out diskNumber) && diskNumber >= 0;
    }

    internal static SanitizationPlan AnalyzePhysicalDiskNumber(int diskNumber)
    {
        if (diskNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(diskNumber));

        string physicalDrive = @"\\.\PHYSICALDRIVE" + diskNumber.ToString();
        Dictionary<string, string> info = QueryDisk(physicalDrive);

        if (info.Count == 0 ||
            string.IsNullOrWhiteSpace(Get(info, "Model")) &&
            string.IsNullOrWhiteSpace(Get(info, "PNPDeviceID")))
        {
            throw new IOException(
                "Physical disk " + diskNumber + " could not be queried safely.");
        }

        string model = Get(info, "Model");
        string media = Get(info, "MediaType");
        string iface = Get(info, "InterfaceType");
        string bus = Get(info, "BusType");
        string pnp = Get(info, "PNPDeviceID");
        string serial = Get(info, "SerialNumber");
        string storageMediaType = Get(info, "StorageMediaType");
        string diskNumberText = Get(info, "DiskNumber");

        StorageMediaKind kind = Classify(
            model, media, iface, bus, pnp, storageMediaType);

        string systemRoot =
            Path.GetPathRoot(Environment.SystemDirectory) ?? Environment.SystemDirectory;
        string systemDrive = systemRoot.TrimEnd('\\');
        bool system = string.Equals(
            physicalDrive,
            ResolvePhysicalDrive(systemDrive),
            StringComparison.OrdinalIgnoreCase);

        bool msftSystem = string.Equals(
            Get(info, "IsSystem"),
            "True",
            StringComparison.OrdinalIgnoreCase);

        SanitizationPlan plan = new SanitizationPlan
        {
            DriveRoot = "(no drive letter)",
            PhysicalDrive = physicalDrive,
            DiskNumber = string.IsNullOrWhiteSpace(diskNumberText)
                ? diskNumber.ToString()
                : diskNumberText,
            MediaKind = kind,
            Encrypted = false,
            EncryptionStatus = "NotEvaluated (no drive letter)",
            IsSystemDisk = system || msftSystem,
            Model = model,
            SerialNumber = string.IsNullOrWhiteSpace(serial)
                ? Get(info, "StorageSerialNumber")
                : serial,
            BusType = ResolveBusType(bus, model, pnp, iface),
            WindowsMediaType = string.IsNullOrWhiteSpace(storageMediaType)
                ? media
                : storageMediaType
        };

        return ApplySanitizationDecision(plan);
    }

    // Returns a preflight-only view for a physical disk. No destructive operation
    // is performed. A future device executor must call this method again immediately
    // before execution and must require an explicit, independently-created approval.
    internal static SanitizationPlan PrepareDeviceSanitizePreflight(int diskNumber)
    {
        SanitizationPlan plan = AnalyzePhysicalDiskNumber(diskNumber);

        if (!plan.DeviceCommandRequired)
        {
            plan.SafetyState = SanitizationSafetyState.Blocked;
            plan.SafetyBlockReason =
                "The selected media does not require a device-level command.";
            return plan;
        }

        if (plan.IsSystemDisk)
        {
            plan.SafetyState = SanitizationSafetyState.Blocked;
            plan.SafetyBlockReason =
                "Automatic device sanitization is blocked for the running Windows disk.";
            return plan;
        }

        if (plan.MediaKind == StorageMediaKind.Unknown ||
            plan.MediaKind == StorageMediaKind.Virtual)
        {
            plan.SafetyState = SanitizationSafetyState.Blocked;
            plan.SafetyBlockReason =
                "Media type is not sufficiently identified for a device-level operation.";
            return plan;
        }

        // Perform a device-specific READ-ONLY capability probe before allowing
        // the plan to be considered eligible for a future device executor.
        if (plan.MediaKind == StorageMediaKind.Nvme)
        {
            ApplyNvmeCapability(plan);

            if (plan.DeviceSanitizeCapability != DeviceSanitizeCapability.Supported)
            {
                plan.SafetyState = SanitizationSafetyState.Blocked;
                plan.SafetyBlockReason =
                    "NVMe SANICAP does not report a supported sanitize action, or capability probing did not complete. " +
                    (plan.NvmeCapabilityDetail ?? "");
                return plan;
            }
        }
        else
        {
            plan.DeviceSanitizeCapability = DeviceSanitizeCapability.NotEvaluated;
            plan.SafetyState = SanitizationSafetyState.PreflightOnly;
            plan.SafetyBlockReason =
                "Device capability is media-specific and has not been probed for this non-NVMe device.";
        }

        return plan;
    }

    // Explicitly reports whether this protocol file is allowed to execute a
    // destructive device operation. It is intentionally always false here.
    // This keeps --media-test and the decision layer non-destructive.
    internal static bool CanExecuteDeviceCommand(SanitizationPlan plan)
    {
        return false;
    }

    private static SanitizationPlan ApplySanitizationDecision(
        SanitizationPlan plan)
    {
        plan.DeviceSanitizeCapability = DeviceSanitizeCapability.NotEvaluated;
        plan.SafetyState = SanitizationSafetyState.PreflightOnly;
        plan.SafetyBlockReason =
            "Analysis only; no destructive device command is enabled.";

        // A disk containing the currently running Windows system volume is
        // never eligible for an in-OS sanitization operation. This is a hard
        // decision gate and must run before media-specific logic.
        if (plan.IsSystemDisk)
        {
            plan.RecommendedStrength = SanitizationStrength.Blocked;
            plan.RecommendedMethod =
                "BLOCKED: running Windows system disk; offline/boot environment required";
            plan.Reason =
                "The target physical disk contains the running Windows system volume. " +
                "Device-level sanitization must not be attempted from the running OS.";
            plan.DeviceCommandRequired = false;
            plan.SafetyState = SanitizationSafetyState.Blocked;
            plan.SafetyBlockReason =
                "Running Windows disk: sanitization is blocked until an offline/boot environment is used.";
            return plan;
        }

        if (plan.MediaKind == StorageMediaKind.Virtual)
        {
            plan.RecommendedStrength = SanitizationStrength.CryptographicErase;
            plan.RecommendedMethod =
                "Provider-level cryptographic erase / storage-provider sanitization";
            plan.Reason =
                "Virtual storage depends on the underlying storage provider; physical-device commands are not selected.";
            plan.DeviceCommandRequired = true;
            return plan;
        }

        if (plan.Encrypted &&
            (plan.MediaKind == StorageMediaKind.SataSsd ||
             plan.MediaKind == StorageMediaKind.Nvme))
        {
            plan.RecommendedStrength = SanitizationStrength.CryptographicErase;
            plan.RecommendedMethod = plan.MediaKind == StorageMediaKind.Nvme
                ? "NVMe Sanitize / Crypto Erase (device-level, when supported)"
                : "ATA Sanitize Crypto Scramble / TCG Opal Crypto Erase (when supported)";
            plan.Reason =
                "BitLocker-encrypted flash media is best handled by a device-level cryptographic sanitization capability when the device supports it.";
            plan.DeviceCommandRequired = true;
            return plan;
        }

        if (plan.MediaKind == StorageMediaKind.Nvme)
        {
            plan.RecommendedStrength = SanitizationStrength.DevicePurge;
            plan.DeviceCommandRequired = true;

            // The decision is based on the controller's actual SANICAP value,
            // not on the model name or on the fact that the device is NVMe.
            ApplyNvmeCapability(plan);

            if (plan.DeviceSanitizeCapability == DeviceSanitizeCapability.Supported)
            {
                plan.RecommendedMethod = GetNvmePreferredMethod(plan);
                plan.Reason =
                    "NVMe Identify Controller SANICAP reports at least one supported sanitize action. " +
                    "A future device executor must still perform an independent preflight and explicit approval.";
                plan.SafetyState = SanitizationSafetyState.PreflightOnly;
                plan.SafetyBlockReason =
                    "Capability is supported, but this decision layer never executes a device command.";
            }
            else if (plan.DeviceSanitizeCapability == DeviceSanitizeCapability.Unsupported)
            {
                // Do not advertise DevicePurge as a recommendation when the
                // controller reports that no NVMe Sanitize action exists.
                plan.RecommendedStrength = SanitizationStrength.Blocked;
                plan.DeviceCommandRequired = false;
                plan.RecommendedMethod =
                    "BLOCKED: NVMe Sanitize is not reported by SANICAP; no safe device-level method is available.";
                plan.Reason =
                    "NVMe flash media is not reliably purged by ordinary file overwrite, and this controller reports SANICAP=0x" +
                    plan.NvmeSanitizeCapabilitiesRaw.ToString("X8") + ".";
                plan.SafetyState = SanitizationSafetyState.Blocked;
                plan.SafetyBlockReason =
                    "NVMe SANICAP reports no Crypto Erase, Block Erase, or Overwrite sanitize capability.";
            }
            else
            {
                plan.RecommendedStrength = SanitizationStrength.Blocked;
                plan.DeviceCommandRequired = false;
                plan.RecommendedMethod =
                    "BLOCKED: NVMe sanitize capability could not be determined safely.";
                plan.Reason =
                    "NVMe flash media is not reliably purged by ordinary file overwrite, but the read-only capability probe did not complete.";
                plan.SafetyState = SanitizationSafetyState.Blocked;
                plan.SafetyBlockReason =
                    "NVMe capability is UNKNOWN; refuse to claim device-level purge.";
            }

            return plan;
        }

        if (plan.MediaKind == StorageMediaKind.SataSsd)
        {
            plan.RecommendedStrength = SanitizationStrength.DevicePurge;
            plan.RecommendedMethod =
                "ATA sanitize / secure erase or vendor-approved flash purge";
            plan.Reason =
                "SATA flash translation layers can leave unmapped physical blocks untouched by file overwrite.";
            plan.DeviceCommandRequired = true;
            return plan;
        }

        if (plan.MediaKind == StorageMediaKind.UsbFlash)
        {
            plan.RecommendedStrength = SanitizationStrength.DevicePurge;
            plan.RecommendedMethod =
                "USB device/bridge-specific purge or vendor-approved flash sanitization";
            plan.Reason =
                "USB storage bridges do not guarantee ATA/NVMe command pass-through; a future executor must use a device-specific method and independently revalidate the USB target.";
            plan.DeviceCommandRequired = true;
            return plan;
        }

        if (plan.MediaKind == StorageMediaKind.Hdd)
        {
            plan.RecommendedStrength = SanitizationStrength.LogicalClear;
            plan.RecommendedMethod =
                "HDD clear: full-addressable-area overwrite + verification";
            plan.Reason =
                "Magnetic HDD media can use validated overwrite-based clearing.";
            plan.DeviceCommandRequired = false;
            return plan;
        }

        plan.RecommendedStrength = SanitizationStrength.LogicalClear;
        plan.RecommendedMethod =
            "V2 logical clear + verification; device purge unavailable until media is identified";
        plan.Reason =
            "Unknown storage type; refuse to claim device-level purge.";
        plan.DeviceCommandRequired = false;
        return plan;
    }

    private static void ApplyNvmeCapability(SanitizationPlan plan)
    {
        if (plan == null || plan.MediaKind != StorageMediaKind.Nvme)
            return;

        if (string.IsNullOrWhiteSpace(plan.PhysicalDrive))
        {
            plan.DeviceSanitizeCapability = DeviceSanitizeCapability.Unknown;
            plan.NvmeCapabilityDetail = "Physical disk path is missing.";
            return;
        }

        NvmeSanitizeCapabilityResult capability =
            NvmeCapabilityProbe.Probe(plan.PhysicalDrive);

        plan.NvmeSanitizeCapabilitiesRaw = capability.SanitizeCapabilitiesRaw;
        plan.NvmeCryptoEraseSupported = capability.CryptoErase;
        plan.NvmeBlockEraseSupported = capability.BlockErase;
        plan.NvmeOverwriteSupported = capability.Overwrite;
        plan.NvmeNdi = capability.NoDeallocateInhibited;
        plan.NvmeNodmmasModifiesMedia =
            capability.NoDeallocateAfterSanitizeModifiesMedia;
        plan.NvmeCapabilityDetail = capability.Detail ?? "";

        switch (capability.Status)
        {
            case NvmeSanitizeCapabilityStatus.Supported:
                plan.DeviceSanitizeCapability = DeviceSanitizeCapability.Supported;
                break;
            case NvmeSanitizeCapabilityStatus.Unsupported:
                plan.DeviceSanitizeCapability = DeviceSanitizeCapability.Unsupported;
                break;
            default:
                plan.DeviceSanitizeCapability = DeviceSanitizeCapability.Unknown;
                break;
        }
    }

    private static string GetNvmePreferredMethod(SanitizationPlan plan)
    {
        if (plan.NvmeCryptoEraseSupported)
            return "NVMe Sanitize — Crypto Erase (controller-reported)";

        if (plan.NvmeBlockEraseSupported)
            return "NVMe Sanitize — Block Erase (controller-reported)";

        if (plan.NvmeOverwriteSupported)
            return "NVMe Sanitize — Overwrite (controller-reported)";

        return "NVMe Sanitize (controller-reported)";
    }

    private static string ResolvePhysicalDrive(string drive)
    {
        // Avoid ASSOCIATORS queries here. Some Windows/WMI configurations
        // expose the association classes inconsistently. Reading the two
        // association classes directly is more tolerant.
        try
        {
            List<string> partitionIds = new List<string>();

            using (ManagementObjectSearcher logicalSearcher =
                new ManagementObjectSearcher(
                    "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
            using (ManagementObjectCollection mappings = logicalSearcher.Get())
            {
                foreach (ManagementObject mapping in mappings)
                {
                    string dependent = Convert.ToString(mapping["Dependent"]);
                    if (string.IsNullOrWhiteSpace(dependent))
                        continue;

                    // Example:
                    // \\COMPUTER\root\cimv2:Win32_LogicalDisk.DeviceID="C:"
                    if (!dependent.Contains("DeviceID=\"" + drive + "\""))
                        continue;

                    string antecedent = Convert.ToString(mapping["Antecedent"]);
                    string partitionId = ExtractWmiPropertyValue(
                        antecedent, "DeviceID");

                    if (!string.IsNullOrWhiteSpace(partitionId))
                        partitionIds.Add(partitionId);
                }
            }

            if (partitionIds.Count == 0)
                return null;

            using (ManagementObjectSearcher diskSearcher =
                new ManagementObjectSearcher(
                    "SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition"))
            using (ManagementObjectCollection mappings = diskSearcher.Get())
            {
                foreach (ManagementObject mapping in mappings)
                {
                    string antecedent = Convert.ToString(mapping["Antecedent"]);
                    string diskId = ExtractWmiPropertyValue(
                        antecedent, "DeviceID");

                    string dependent = Convert.ToString(mapping["Dependent"]);
                    string partitionId = ExtractWmiPropertyValue(
                        dependent, "DeviceID");

                    if (string.IsNullOrWhiteSpace(diskId) ||
                        string.IsNullOrWhiteSpace(partitionId))
                        continue;

                    foreach (string wanted in partitionIds)
                    {
                        if (string.Equals(
                            wanted,
                            partitionId,
                            StringComparison.OrdinalIgnoreCase))
                            return diskId;
                    }
                }
            }
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string ExtractWmiPropertyValue(
        string associationPath,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(associationPath))
            return "";

        string marker = propertyName + "=\"";
        int start = associationPath.IndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);

        if (start < 0)
            return "";

        start += marker.Length;
        int end = associationPath.IndexOf('"', start);

        if (end < 0)
            return "";

        return associationPath.Substring(start, end - start)
            .Replace("\\\\", "\\");
    }

    private static Dictionary<string, string> QueryDisk(string deviceId)
    {
        Dictionary<string, string> result =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // IMPORTANT: deviceId is a DOS device path such as
        // \\.\PHYSICALDRIVE0. It must be queried through WQL; passing it
        // directly to ManagementObject() makes WMI interpret it as a WMI
        // object path and can produce "Invalid namespace".
        using (ManagementObjectSearcher searcher =
            new ManagementObjectSearcher(
                "SELECT DeviceID, Model, MediaType, InterfaceType, " +
                "PNPDeviceID, SerialNumber FROM Win32_DiskDrive"))
        using (ManagementObjectCollection disks = searcher.Get())
        {
            foreach (ManagementObject disk in disks)
            {
                string currentId = Convert.ToString(disk["DeviceID"]);

                if (!string.Equals(
                    currentId,
                    deviceId,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                result["Model"] = Convert.ToString(disk["Model"]);
                result["MediaType"] = Convert.ToString(disk["MediaType"]);
                result["InterfaceType"] = Convert.ToString(disk["InterfaceType"]);
                result["PNPDeviceID"] = Convert.ToString(disk["PNPDeviceID"]);
                result["SerialNumber"] = Convert.ToString(disk["SerialNumber"]);
                break;
            }
        }

        string diskNumber = GetDiskNumber(deviceId);
        result["DiskNumber"] = diskNumber;

        if (!string.IsNullOrWhiteSpace(diskNumber))
        {
            try
            {
                Dictionary<string, string> storage = QueryMsftDisk(diskNumber);
                foreach (KeyValuePair<string, string> item in storage)
                    result[item.Key] = item.Value;
            }
            catch (ManagementException)
            {
                // MSFT_Storage is optional. Win32_DiskDrive data remains valid.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return result;
    }

    private static Dictionary<string, string> QueryMsftDisk(string diskNumber)
    {
        Dictionary<string, string> result =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            ManagementScope scope =
                new ManagementScope(@"root\Microsoft\Windows\Storage");
            scope.Connect();

            ObjectQuery query = new ObjectQuery(
                "SELECT Number, FriendlyName, SerialNumber, BusType, MediaType, " +
                "IsBoot, IsSystem, HealthStatus, OperationalStatus " +
                "FROM MSFT_Disk WHERE Number=" + diskNumber);

            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(scope, query))
            using (ManagementObjectCollection disks = searcher.Get())
            {
                foreach (ManagementObject disk in disks)
                {
                    result["StorageMediaType"] =
                        Convert.ToString(disk["MediaType"]);
                    result["BusType"] =
                        TranslateBusType(Convert.ToString(disk["BusType"]));
                    result["FriendlyName"] =
                        Convert.ToString(disk["FriendlyName"]);
                    result["StorageSerialNumber"] =
                        Convert.ToString(disk["SerialNumber"]);
                    result["IsBoot"] =
                        Convert.ToString(disk["IsBoot"]);
                    result["IsSystem"] =
                        Convert.ToString(disk["IsSystem"]);
                    result["HealthStatus"] =
                        Convert.ToString(disk["HealthStatus"]);
                    result["OperationalStatus"] =
                        Convert.ToString(disk["OperationalStatus"]);
                    break;
                }
            }
        }
        catch
        {
            // Some Windows installations expose MSFT_Storage through CIM
            // but not through the legacy System.Management provider.
            // QueryDisk still supplies Win32_DiskDrive data, and
            // ResolveBusType() below provides a safe fallback.
        }

        return result;
    }

    private static string ResolveBusType(
        string storageBus,
        string model,
        string pnp,
        string interfaceType)
    {
        // Windows can expose NVMe devices through a SCSI storage path
        // (for example SCSI\DISK&VEN_NVME...). For sanitization decisions,
        // an explicit NVMe signature in the model/PNP path must win over
        // the generic SCSI interface label.
        string combined =
            ((model ?? "") + " " + (pnp ?? "")).ToUpperInvariant();

        if (combined.Contains("NVME") ||
            combined.Contains("NVM EXPRESS") ||
            combined.Contains("VEN_NVME") ||
            combined.Contains("PROD_NVME"))
            return "NVMe";

        if (combined.Contains("USBSTOR") ||
            combined.Contains("USB "))
            return "USB";

        if (combined.Contains("SATA"))
            return "SATA";

        // Prefer a real MSFT_Disk numeric BusType when available.
        if (!string.IsNullOrWhiteSpace(storageBus) &&
            !IsNumericBusValue(storageBus))
            return storageBus;

        if (!string.IsNullOrWhiteSpace(storageBus) &&
            IsNumericBusValue(storageBus))
            return TranslateBusType(storageBus);

        return interfaceType ?? "";
    }

    private static bool IsNumericBusValue(string value)
    {
        int number;
        return int.TryParse(value, out number);
    }

    private static string GetDiskNumber(string deviceId)
    {
        const string prefix = @"\\.\PHYSICALDRIVE";
        if (deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return deviceId.Substring(prefix.Length);

        return "";
    }

    private static string TranslateBusType(string value)
    {
        int number;
        if (!int.TryParse(value, out number))
            return value ?? "";

        switch (number)
        {
            case 0: return "Unknown";
            case 1: return "SCSI";
            case 2: return "ATAPI";
            case 3: return "ATA";
            case 4: return "IEEE1394";
            case 5: return "SSA";
            case 6: return "FibreChannel";
            case 7: return "USB";
            case 8: return "RAID";
            case 9: return "iSCSI";
            case 10: return "SAS";
            case 11: return "SATA";
            case 12: return "SD";
            case 13: return "MMC";
            case 14: return "Virtual";
            case 15: return "FileBackedVirtual";
            case 16: return "Spaces";
            case 17: return "NVMe";
            case 18: return "SCM";
            case 19: return "UFS";
            case 20: return "Max";
            default: return value;
        }
    }

    private static StorageMediaKind Classify(
        string model,
        string media,
        string iface,
        string bus,
        string pnp,
        string storageMediaType)
    {
        string s =
            ((model ?? "") + " " +
             (media ?? "") + " " +
             (iface ?? "") + " " +
             (bus ?? "") + " " +
             (pnp ?? "") + " " +
             (storageMediaType ?? ""))
            .ToUpperInvariant();

        if (s.Contains("VIRTUAL") ||
            s.Contains("VHD") ||
            s.Contains("VMWARE") ||
            s.Contains("VBOX") ||
            s.Contains("HYPER-V") ||
            s.Contains("FILEBACKEDVIRTUAL"))
            return StorageMediaKind.Virtual;

        int mediaNumber;
        if (int.TryParse(storageMediaType, out mediaNumber))
        {
            if (mediaNumber == 3)
                return StorageMediaKind.Hdd;

            if (mediaNumber == 4)
            {
                if (s.Contains("NVME") || s.Contains("NVM EXPRESS"))
                    return StorageMediaKind.Nvme;

                if (s.Contains("USB") && !s.Contains("SATA"))
                    return StorageMediaKind.UsbFlash;

                return StorageMediaKind.SataSsd;
            }
        }

        if (s.Contains("NVME") ||
            s.Contains("NVM EXPRESS") ||
            s.Contains("BUS NVME") ||
            s.Contains("VEN_NVME"))
            return StorageMediaKind.Nvme;

        if (s.Contains("USB") || s.Contains("FLASH"))
        {
            if (s.Contains("HDD") || s.Contains("HARD DISK"))
                return StorageMediaKind.Hdd;

            return StorageMediaKind.UsbFlash;
        }

        if (s.Contains("SSD") || s.Contains("SOLID STATE"))
            return StorageMediaKind.SataSsd;

        if (s.Contains("HDD") || s.Contains("HARD DISK"))
            return StorageMediaKind.Hdd;

        return StorageMediaKind.Unknown;
    }

    private static bool IsEncrypted(string drive)
    {
        string status = GetEncryptionStatus(drive);
        return status == "FullyEncrypted" ||
               status == "EncryptionInProgress" ||
               status == "EncryptionPaused" ||
               status == "DecryptionInProgress" ||
               status == "DecryptionPaused" ||
               status == "Protected";
    }

    private static string GetEncryptionStatus(string drive)
    {
        try
        {
            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    @"root\CIMV2\Security\MicrosoftVolumeEncryption",
                    "SELECT DriveLetter, ProtectionStatus, ConversionStatus " +
                    "FROM Win32_EncryptableVolume"))
            using (ManagementObjectCollection volumes = searcher.Get())
            {
                foreach (ManagementObject v in volumes)
                {
                    if (!string.Equals(
                        Convert.ToString(v["DriveLetter"]),
                        drive,
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    uint conversion =
                        v["ConversionStatus"] == null
                            ? 0u
                            : Convert.ToUInt32(v["ConversionStatus"]);

                    uint protection =
                        v["ProtectionStatus"] == null
                            ? 0u
                            : Convert.ToUInt32(v["ProtectionStatus"]);

                    if (protection == 1u)
                        return "Protected";

                    switch (conversion)
                    {
                        case 1u: return "FullyEncrypted";
                        case 2u: return "EncryptionInProgress";
                        case 3u: return "DecryptionInProgress";
                        case 4u: return "EncryptionPaused";
                        case 5u: return "DecryptionPaused";
                        default: return "NotEncrypted";
                    }
                }
            }
        }
        catch
        {
            return "Unavailable";
        }

        return "NotBitLockerVolume";
    }

    private static bool IsSystemDrive(string drive)
    {
        string system = Path.GetPathRoot(Environment.SystemDirectory);
        return string.Equals(
            system,
            drive + "\\",
            StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetSafetySummary(SanitizationPlan plan)
    {
        if (plan == null)
            return "No sanitization plan is available.";

        if (plan.SafetyState == SanitizationSafetyState.Blocked)
            return "BLOCKED: " + (plan.SafetyBlockReason ?? "Safety policy blocked the operation.");

        if (plan.SafetyState == SanitizationSafetyState.PreflightOnly)
            return "PREFLIGHT ONLY: " + (plan.SafetyBlockReason ?? "Device capability must be probed by a separate executor.");

        return "READY FOR SEPARATE EXECUTOR: this protocol does not execute device commands.";
    }

    private static string Get(
        Dictionary<string, string> d,
        string key)
    {
        string value;
        return d.TryGetValue(key, out value) ? value ?? "" : "";
    }

    private static string EscapeWmi(string value)
    {
        return (value ?? "")
            .Replace("\\", "\\\\")
            .Replace("'", "\\'");
    }
}

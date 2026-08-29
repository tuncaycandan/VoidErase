using System;
using System.IO;
using System.Management;

namespace VoidErase;

internal enum StorageMediaType
{
    Unknown,
    HDD,
    SSD,
    NVMe,
    Removable
}

internal enum SanitizationMethod
{
    FileLevel,
    CryptographicErase,
    DeviceSanitize,
    Unknown
}

internal sealed class StorageSanitizationInfo
{
    public StorageMediaType MediaType { get; init; }
    public SanitizationMethod RecommendedMethod { get; init; }
    public string DeviceModel { get; init; } = "";
    public string InterfaceType { get; init; } = "";
    public bool IsRemovable { get; init; }

    public string MediaTypeText
    {
        get
        {
            return MediaType switch
            {
                StorageMediaType.HDD => "HDD",
                StorageMediaType.SSD => "SSD",
                StorageMediaType.NVMe => "NVMe",
                StorageMediaType.Removable => "Removable",
                _ => "Unknown"
            };
        }
    }

    public string MethodText
    {
        get
        {
            return RecommendedMethod switch
            {
                SanitizationMethod.FileLevel =>
                    "File-level sanitization",

                SanitizationMethod.CryptographicErase =>
                    "Cryptographic Erase",

                SanitizationMethod.DeviceSanitize =>
                    "Device Sanitize",

                _ => "Unknown"
            };
        }
    }
}

internal static class StorageSanitization
{
    public static StorageSanitizationInfo Detect(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);

            string root = Path.GetPathRoot(fullPath)
                ?? throw new InvalidOperationException(
                    "Storage root could not be determined.");

            string driveLetter =
                root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            using ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    "SELECT Model, InterfaceType, MediaType, PNPDeviceID FROM Win32_DiskDrive");

            foreach (ManagementObject disk in searcher.Get())
            {
                string model =
                    disk["Model"]?.ToString() ?? "";

                string interfaceType =
                    disk["InterfaceType"]?.ToString() ?? "";

                string mediaType =
                    disk["MediaType"]?.ToString() ?? "";

                string pnp =
                    disk["PNPDeviceID"]?.ToString() ?? "";

                StorageMediaType detected =
                    DetectMediaType(
                        model,
                        interfaceType,
                        mediaType,
                        pnp);

                if (detected == StorageMediaType.Unknown)
                    continue;

                return new StorageSanitizationInfo
                {
                    MediaType = detected,
                    RecommendedMethod =
                        GetRecommendedMethod(detected),
                    DeviceModel = model,
                    InterfaceType = interfaceType,
                    IsRemovable =
                        detected == StorageMediaType.Removable
                };
            }
        }
        catch
        {
            // Detection failure must never cause data loss.
        }

        return new StorageSanitizationInfo
        {
            MediaType = StorageMediaType.Unknown,
            RecommendedMethod = SanitizationMethod.FileLevel
        };
    }

    private static StorageMediaType DetectMediaType(
        string model,
        string interfaceType,
        string mediaType,
        string pnp)
    {
        string combined =
            $"{model} {interfaceType} {mediaType} {pnp}"
                .ToUpperInvariant();

        if (combined.Contains("NVME"))
            return StorageMediaType.NVMe;

        if (combined.Contains("SSD") ||
            combined.Contains("SOLID STATE"))
        {
            return StorageMediaType.SSD;
        }

        if (combined.Contains("USB") ||
            combined.Contains("REMOVABLE"))
        {
            return StorageMediaType.Removable;
        }

        if (combined.Contains("HDD") ||
            combined.Contains("HARD DISK"))
        {
            return StorageMediaType.HDD;
        }

        return StorageMediaType.Unknown;
    }

    private static SanitizationMethod GetRecommendedMethod(
        StorageMediaType mediaType)
    {
        return mediaType switch
        {
            StorageMediaType.NVMe =>
                SanitizationMethod.DeviceSanitize,

            StorageMediaType.SSD =>
                SanitizationMethod.DeviceSanitize,

            StorageMediaType.HDD =>
                SanitizationMethod.FileLevel,

            StorageMediaType.Removable =>
                SanitizationMethod.FileLevel,

            _ =>
                SanitizationMethod.FileLevel
        };
    }
}
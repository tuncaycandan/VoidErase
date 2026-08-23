using System;
using System.IO;
using System.Management;

internal sealed class MediaInfo
{
    public MediaKind Kind { get; set; } = MediaKind.Unknown;
    public string DriveLetter { get; set; } = "";
    public string Model { get; set; } = "";
    public string BusType { get; set; } = "";
    public string MediaType { get; set; } = "";
    public bool IsSystemDrive { get; set; }
    public bool IsRemovable { get; set; }
    public bool IsSolidState { get; set; }
    public bool IsVirtual { get; set; }
}

internal static class MediaDetection
{
    public static MediaInfo Detect(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ?? "";

        if (string.IsNullOrWhiteSpace(root))
            return new MediaInfo();

        string drive = root.TrimEnd('\\');

        if (drive.Length < 2 || drive[1] != ':')
            return new MediaInfo();

        string driveLetter = drive.Substring(0, 2);

        MediaInfo result = new MediaInfo
        {
            DriveLetter = driveLetter
        };

        try
        {
            string systemRoot =
                Environment.GetEnvironmentVariable("SystemDrive") ?? "";

            result.IsSystemDrive =
                string.Equals(
                    systemRoot.TrimEnd('\\'),
                    driveLetter,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
        }

        try
        {
            DriveInfo info = new DriveInfo(driveLetter);

            result.IsRemovable =
                info.DriveType == DriveType.Removable;

            // Windows, bazı USB flash aygıtlarını sabit disk gibi raporlayabilir.
            // Removable bilgisi mevcutsa donanım metni bunu geçersiz kılamaz.
            if (result.IsRemovable)
                result.Kind = MediaKind.Removable;

            if (info.DriveType == DriveType.Network)
            {
                result.Kind = MediaKind.Virtual;
                result.IsVirtual = true;
                return result;
            }

            if (info.DriveType == DriveType.CDRom)
            {
                result.Kind = MediaKind.Optical;
                return result;
            }
        }
        catch
        {
        }

        try
        {
            string escapedDrive =
                driveLetter.Substring(0, 1).Replace("'", "''");

            string partitionQuery =
                "ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" +
                escapedDrive +
                ":'} WHERE AssocClass=Win32_LogicalDiskToPartition";

            using (ManagementObjectSearcher partitionSearcher =
                new ManagementObjectSearcher(partitionQuery))
            {
                foreach (ManagementObject partition in partitionSearcher.Get())
                {
                    string partitionDevice =
                        partition["DeviceID"] as string ?? "";

                    if (string.IsNullOrWhiteSpace(partitionDevice))
                        continue;

                    string diskQuery =
                        "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" +
                        partitionDevice.Replace("'", "''") +
                        "'} WHERE AssocClass=Win32_DiskDriveToDiskPartition";

                    using (ManagementObjectSearcher diskSearcher =
                        new ManagementObjectSearcher(diskQuery))
                    {
                        foreach (ManagementObject disk in diskSearcher.Get())
                        {
                            string model =
                                disk["Model"] as string ?? "";

                            string mediaType =
                                disk["MediaType"] as string ?? "";

                            string interfaceType =
                                disk["InterfaceType"] as string ?? "";

                            string pnpId =
                                disk["PNPDeviceID"] as string ?? "";

                            result.Model = model;
                            result.MediaType = mediaType;
                            result.BusType = interfaceType;

                            string combined =
                                (model + " " +
                                 mediaType + " " +
                                 interfaceType + " " +
                                 pnpId)
                                .ToLowerInvariant();

                            if (combined.Contains("virtual") ||
                                combined.Contains("vmware") ||
                                combined.Contains("virtual disk") ||
                                combined.Contains("hyper-v") ||
                                combined.Contains("microsoft virtual"))
                            {
                                result.IsVirtual = true;
                                result.Kind = MediaKind.Virtual;
                                return result;
                            }

                                                        if (!result.IsRemovable &&
                                (combined.Contains("nvme") ||
                                 combined.Contains("ssd") ||
                                 combined.Contains("solid state")))

                            {
                                result.IsSolidState = true;
                                result.Kind = MediaKind.SolidState;
                            }
                            else if (!result.IsRemovable)
                            {
                                result.Kind = MediaKind.Magnetic;
                            }

                            return result;
                        }
                    }
                }
            }
        }
        catch
        {
            // Donanım bilgisi okunamazsa Unknown bırakılır.
            // Güvenlik açısından başka bir diskin bilgisine düşmüyoruz.
        }

        if (result.IsRemovable &&
            result.Kind == MediaKind.Unknown)
        {
            result.Kind = MediaKind.Removable;
        }

        return result;
    }
}
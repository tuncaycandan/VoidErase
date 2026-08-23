using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal enum NvmeSanitizeCapabilityStatus
{
    NotApplicable,
    Supported,
    Unsupported,
    Unknown
}

internal sealed class NvmeSanitizeCapabilityResult
{
    internal NvmeSanitizeCapabilityStatus Status { get; set; }
    internal bool CryptoErase { get; set; }
    internal bool BlockErase { get; set; }
    internal bool Overwrite { get; set; }
    internal bool NoDeallocateInhibited { get; set; }
    internal bool NoDeallocateAfterSanitizeModifiesMedia { get; set; }
    internal uint SanitizeCapabilitiesRaw { get; set; }
    internal string PreferredMethod { get; set; } = "Unknown";
    internal string Detail { get; set; } = "";
}

// READ-ONLY NVMe Identify Controller capability probe.
//
// Important:
// - Uses IOCTL_STORAGE_QUERY_PROPERTY only.
// - Opens the physical disk with GENERIC_READ.
// - Does NOT issue NVMe Sanitize, Format NVM, Dataset Management,
//   or any other write/destructive command.
// - Uses the returned STORAGE_PROTOCOL_DATA_DESCRIPTOR offsets rather
//   than assuming the protocol payload always starts at one fixed offset.
internal static class NvmeCapabilityProbe
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    private const uint StorageAdapterProtocolSpecificProperty = 49;
    private const uint StorageDeviceProtocolSpecificProperty = 50;
    private const uint PropertyStandardQuery = 0;

    private const uint ProtocolTypeNvme = 3;
    private const uint NvmeDataTypeIdentify = 1;
    private const uint NvmeIdentifyCnsController = 1;

    private const uint GenericRead = 0x80000000;
    private const uint FileAttributeNormal = 0x00000080;

    private const int StoragePropertyQuerySize = 8;
    private const int StorageProtocolSpecificDataSize = 36;

    // Microsoft documents NVMe Identify Controller as a 4096-byte structure.
    private const int IdentifyControllerSize = 4096;

    // STORAGE_PROTOCOL_DATA_DESCRIPTOR:
    // Version (4) + Size (4) + STORAGE_PROTOCOL_SPECIFIC_DATA (36).
    private const int DescriptorHeaderSize = 8;

    // NVMe Identify Controller SANICAP is DWORD at byte 328.
    private const int SanitizeCapabilitiesOffset = 328;

    internal static NvmeSanitizeCapabilityResult Probe(string physicalDrive)
    {
        if (string.IsNullOrWhiteSpace(physicalDrive))
            return Unknown("Physical disk path is missing.");

        if (!physicalDrive.StartsWith(
                @"\\.\PHYSICALDRIVE",
                StringComparison.OrdinalIgnoreCase))
        {
            return Unknown("The supplied path is not a physical-disk DOS path.");
        }

        using (SafeFileHandle handle = CreateFile(
            physicalDrive,
            GenericRead,
            (uint)(FileShare.Read | FileShare.Write),
            IntPtr.Zero,
            FileMode.Open,
            FileAttributeNormal,
            IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                return Unknown(
                    "Physical disk could not be opened for the read-only capability query. " +
                    "Win32 error: " + error + " (" + Win32ErrorName(error) + ").");
            }

            // First use Microsoft's controller/adapter form.
            NvmeSanitizeCapabilityResult adapter =
                QueryIdentifyController(
                    handle,
                    StorageAdapterProtocolSpecificProperty);

            if (adapter.Status != NvmeSanitizeCapabilityStatus.Unknown)
                return adapter;

            // Some Windows storage paths expose the protocol query through
            // the device property instead. This is still read-only.
            NvmeSanitizeCapabilityResult device =
                QueryIdentifyController(
                    handle,
                    StorageDeviceProtocolSpecificProperty);

            if (device.Status != NvmeSanitizeCapabilityStatus.Unknown)
                return device;

            return Unknown(
                "Both read-only NVMe Identify query paths were rejected. " +
                "Adapter query: " + adapter.Detail + " " +
                "Device query: " + device.Detail);
        }
    }

    private static NvmeSanitizeCapabilityResult QueryIdentifyController(
        SafeFileHandle handle,
        uint propertyId)
    {
        byte[] buffer = new byte[
            StoragePropertyQuerySize +
            StorageProtocolSpecificDataSize +
            IdentifyControllerSize];

        // STORAGE_PROPERTY_QUERY
        WriteUInt32(buffer, 0, propertyId);
        WriteUInt32(buffer, 4, PropertyStandardQuery);

        // STORAGE_PROTOCOL_SPECIFIC_DATA starts at AdditionalParameters.
        int p = StoragePropertyQuerySize;

        WriteUInt32(buffer, p + 0, ProtocolTypeNvme);
        WriteUInt32(buffer, p + 4, NvmeDataTypeIdentify);
        WriteUInt32(buffer, p + 8, NvmeIdentifyCnsController);
        WriteUInt32(buffer, p + 12, 0);

        // ProtocolDataOffset is relative to the start of
        // STORAGE_PROTOCOL_SPECIFIC_DATA.
        WriteUInt32(buffer, p + 16, StorageProtocolSpecificDataSize);
        WriteUInt32(buffer, p + 20, IdentifyControllerSize);

        WriteUInt32(buffer, p + 24, 0);
        WriteUInt32(buffer, p + 28, 0);
        WriteUInt32(buffer, p + 32, 0);

        int returned;

        if (!DeviceIoControl(
            handle,
            IOCTL_STORAGE_QUERY_PROPERTY,
            buffer,
            buffer.Length,
            buffer,
            buffer.Length,
            out returned,
            IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();

            return Unknown(
                "PropertyId " + propertyId +
                " rejected the read-only NVMe Identify query. " +
                "Win32 error: " + error +
                " (" + Win32ErrorName(error) + ").");
        }

        if (returned < DescriptorHeaderSize + StorageProtocolSpecificDataSize)
        {
            return Unknown(
                "PropertyId " + propertyId +
                " returned too little data (" + returned + " bytes).");
        }

        // STORAGE_PROTOCOL_DATA_DESCRIPTOR
        uint descriptorVersion = ReadUInt32(buffer, 0);
        uint descriptorSize = ReadUInt32(buffer, 4);

        if (descriptorVersion == 0 || descriptorSize < DescriptorHeaderSize)
        {
            return Unknown(
                "PropertyId " + propertyId +
                " returned an invalid storage protocol descriptor header. " +
                "Version=" + descriptorVersion +
                ", Size=" + descriptorSize + ".");
        }

        // The protocol-specific structure is at offset 8.
        int protocolDataStart = DescriptorHeaderSize;

        uint returnedProtocolOffset =
            ReadUInt32(buffer, protocolDataStart + 16);

        uint returnedProtocolLength =
            ReadUInt32(buffer, protocolDataStart + 20);

        if (returnedProtocolOffset < StorageProtocolSpecificDataSize)
        {
            return Unknown(
                "PropertyId " + propertyId +
                " returned an invalid ProtocolDataOffset: " +
                returnedProtocolOffset + ".");
        }

        if (returnedProtocolLength < IdentifyControllerSize)
        {
            return Unknown(
                "PropertyId " + propertyId +
                " returned an insufficient ProtocolDataLength: " +
                returnedProtocolLength + ".");
        }

        long identifyAbsoluteOffset =
            (long)protocolDataStart +
            returnedProtocolOffset;

        if (identifyAbsoluteOffset < 0 ||
            identifyAbsoluteOffset + IdentifyControllerSize > returned)
        {
            return Unknown(
                "PropertyId " + propertyId +
                " returned an Identify payload outside the returned buffer. " +
                "Offset=" + identifyAbsoluteOffset +
                ", Returned=" + returned + ".");
        }

        int identifyOffset = checked((int)identifyAbsoluteOffset);

        // Basic Identify Controller sanity checks:
        // VID at byte 0 and NN at byte 516.
        ushort vid = ReadUInt16(buffer, identifyOffset + 0);
        uint nn = ReadUInt32(buffer, identifyOffset + 516);

        if (vid == 0 || nn == 0)
        {
            return Unknown(
                "PropertyId " + propertyId +
                " returned an Identify Controller payload, but its basic " +
                "VID/NN sanity check failed. VID=0x" +
                vid.ToString("X4") + ", NN=" + nn + ".");
        }

        int sanitizeOffset =
            checked(identifyOffset + SanitizeCapabilitiesOffset);

        if (sanitizeOffset + 4 > returned)
        {
            return Unknown(
                "PropertyId " + propertyId +
                " returned an Identify payload too short for SANICAP.");
        }

        uint sanicap = ReadUInt32(buffer, sanitizeOffset);

        // NVMe Identify Controller SANICAP:
        // bit 0  = Crypto Erase
        // bit 1  = Block Erase
        // bit 2  = Overwrite
        // bit 29 = No-Deallocate Inhibited (NDI)
        // bits 31:30 = NODMMAS.
        bool crypto = (sanicap & 0x1u) != 0;
        bool block = (sanicap & 0x2u) != 0;
        bool overwrite = (sanicap & 0x4u) != 0;
        bool ndi = (sanicap & (1u << 29)) != 0;
        uint nodmmas = (sanicap >> 30) & 0x3u;

        bool supported = crypto || block || overwrite;

        string preferred;

        if (crypto)
            preferred = "NVMe Sanitize — Crypto Erase";
        else if (block)
            preferred = "NVMe Sanitize — Block Erase";
        else if (overwrite)
            preferred = "NVMe Sanitize — Overwrite";
        else
            preferred = "No NVMe Sanitize action reported by SANICAP";

        string detail =
            "Read-only Identify Controller query succeeded using PropertyId " +
            propertyId + ". " +
            "VID=0x" + vid.ToString("X4") +
            ", NN=" + nn +
            ", SANICAP=0x" + sanicap.ToString("X8") + ". " +
            "CryptoErase=" + (crypto ? "Yes" : "No") + ", " +
            "BlockErase=" + (block ? "Yes" : "No") + ", " +
            "Overwrite=" + (overwrite ? "Yes" : "No") + ", " +
            "NDI=" + (ndi ? "Yes" : "No") + ", " +
            "NODMMAS=" + nodmmas + ". " +
            "No destructive command was issued.";

        return new NvmeSanitizeCapabilityResult
        {
            Status = supported
                ? NvmeSanitizeCapabilityStatus.Supported
                : NvmeSanitizeCapabilityStatus.Unsupported,
            CryptoErase = crypto,
            BlockErase = block,
            Overwrite = overwrite,
            NoDeallocateInhibited = ndi,
            NoDeallocateAfterSanitizeModifiesMedia = nodmmas != 0,
            SanitizeCapabilitiesRaw = sanicap,
            PreferredMethod = preferred,
            Detail = detail
        };
    }

    private static NvmeSanitizeCapabilityResult Unknown(string detail)
    {
        return new NvmeSanitizeCapabilityResult
        {
            Status = NvmeSanitizeCapabilityStatus.Unknown,
            PreferredMethod = "Unknown — capability probe did not complete",
            Detail = detail
        };
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return (ushort)(
            buffer[offset] |
            (buffer[offset + 1] << 8));
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return (uint)(
            buffer[offset] |
            (buffer[offset + 1] << 8) |
            (buffer[offset + 2] << 16) |
            (buffer[offset + 3] << 24));
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static string Win32ErrorName(int error)
    {
        switch (error)
        {
            case 5:
                return "ERROR_ACCESS_DENIED";
            case 6:
                return "ERROR_INVALID_HANDLE";
            case 87:
                return "ERROR_INVALID_PARAMETER";
            case 1117:
                return "ERROR_IO_DEVICE";
            case 1:
                return "ERROR_INVALID_FUNCTION";
            default:
                return "Win32";
        }
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);
}

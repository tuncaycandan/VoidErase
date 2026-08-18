using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VoidErase;

/// <summary>
/// Best-effort media classification for the volume containing a target path.
/// This is deliberately advisory: it does not establish NIST sanitization by itself.
/// </summary>
internal static class MediaDetection
{
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint ErrorInsufficientBuffer = 122;

    public static MediaKind Detect(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root))
                return MediaKind.Unknown;

            DriveInfo drive = new DriveInfo(root);

            if (drive.DriveType == DriveType.Network)
                return MediaKind.Virtual;

            if (drive.DriveType == DriveType.Removable)
                return MediaKind.Removable;

            if (drive.DriveType != DriveType.Fixed)
                return MediaKind.Unknown;

            int? diskNumber = TryGetPhysicalDiskNumber(root);
            if (!diskNumber.HasValue)
                return MediaKind.Unknown;

            bool? incursSeekPenalty = TryGetSeekPenalty(diskNumber.Value);
            if (incursSeekPenalty == false)
                return MediaKind.SolidState;

            if (incursSeekPenalty == true)
                return MediaKind.Magnetic;
        }
        catch
        {
            // Classification must never block a deletion operation.
        }

        return MediaKind.Unknown;
    }

    private static int? TryGetPhysicalDiskNumber(string volumeRoot)
    {
        string normalized = volumeRoot.TrimEnd('\\');
        string devicePath = "\\\\.\\" + normalized;

        using SafeFileHandle handle = OpenDevice(devicePath);
        if (handle.IsInvalid)
            return null;

        int size = Marshal.SizeOf(typeof(DiskExtentsBuffer));
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!DeviceIoControl(
                    handle,
                    IoctlVolumeGetVolumeDiskExtents,
                    IntPtr.Zero,
                    0,
                    buffer,
                    size,
                    out int returned,
                    IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorInsufficientBuffer)
                    return null;

                size = 64 * 1024;
                Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal(size);

                if (!DeviceIoControl(
                        handle,
                        IoctlVolumeGetVolumeDiskExtents,
                        IntPtr.Zero,
                        0,
                        buffer,
                        size,
                        out returned,
                        IntPtr.Zero))
                    return null;
            }

            int count = Marshal.ReadInt32(buffer, 0);
            if (count <= 0)
                return null;

            IntPtr firstExtent = IntPtr.Add(buffer, 8);
            DiskExtent extent = Marshal.PtrToStructure<DiskExtent>(firstExtent);
            return extent.DiskNumber;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool? TryGetSeekPenalty(int diskNumber)
    {
        string devicePath = "\\\\.\\PhysicalDrive" + diskNumber;
        using SafeFileHandle handle = OpenDevice(devicePath);
        if (handle.IsInvalid)
            return null;

        StoragePropertyQuery query = new StoragePropertyQuery
        {
            PropertyId = StorageDeviceSeekPenaltyProperty,
            QueryType = PropertyStandardQuery,
            AdditionalParameters = 0
        };

        int querySize = Marshal.SizeOf(typeof(StoragePropertyQuery));
        int descriptorSize = Marshal.SizeOf(typeof(StorageDeviceSeekPenaltyDescriptor));
        IntPtr queryPtr = Marshal.AllocHGlobal(querySize);
        IntPtr descriptorPtr = Marshal.AllocHGlobal(descriptorSize);

        try
        {
            Marshal.StructureToPtr(query, queryPtr, false);

            if (!DeviceIoControl(
                    handle,
                    IoctlStorageQueryProperty,
                    queryPtr,
                    querySize,
                    descriptorPtr,
                    descriptorSize,
                    out _,
                    IntPtr.Zero))
                return null;

            StorageDeviceSeekPenaltyDescriptor descriptor =
                Marshal.PtrToStructure<StorageDeviceSeekPenaltyDescriptor>(descriptorPtr);

            return descriptor.IncursSeekPenalty;
        }
        finally
        {
            Marshal.FreeHGlobal(queryPtr);
            Marshal.FreeHGlobal(descriptorPtr);
        }
    }

    private static SafeFileHandle OpenDevice(string path)
    {
        return CreateFile(
            path,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        int inputBufferSize,
        IntPtr outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceSeekPenaltyDescriptor
    {
        public int Version;
        public int Size;
        [MarshalAs(UnmanagedType.I1)]
        public bool IncursSeekPenalty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskExtent
    {
        public int DiskNumber;
        public long StartingOffset;
        public long ExtentLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskExtentsBuffer
    {
        public int NumberOfDiskExtents;
        public int Reserved;
        public DiskExtent FirstExtent;
    }
}

internal sealed class SafeFileHandle : SafeHandle
{
    public SafeFileHandle() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

    protected override bool ReleaseHandle() => CloseHandle(handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

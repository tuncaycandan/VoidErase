using VoidErase;
using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;


internal interface IProgressReporter
{
    void ReportProgress(long processed, long total, TimeSpan elapsed);
    void ReportValidation(long current, long total, TimeSpan elapsed);
    void ReportFinalizing();
    void ThrowIfCancellationRequested();
}


internal static class L
{
    private static bool _english;

    public static bool English => _english;

    private const string KeyPath = @"Software\VoidErase";
    private const string ValueName = "Language";
    private const string ConfirmValue = "ConfirmBeforeErase";
    private const string AutoUpdateValue = "AutoUpdate";
	private const string DeleteHiddenValue = "DeleteHiddenFiles";
	private const string DeleteHiddenFilesValue = "DeleteHiddenFiles";

    public static bool ConfirmBeforeErase { get; private set; } = true;
    public static bool AutoUpdate { get; private set; } = true;
	public static bool DeleteHiddenFiles { get; private set; } = false;
    public static bool Turkish { get; private set; }

    static L()
    {
        Load();
    }




    public static void Load()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            string? value = key?.GetValue(ValueName) as string;

            if (string.Equals(value, "tr", StringComparison.OrdinalIgnoreCase))
                Turkish = true;
            else if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase))
                Turkish = false;
            else
                Turkish = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                    .Equals("tr", StringComparison.OrdinalIgnoreCase);

            ConfirmBeforeErase = ReadBool(key, ConfirmValue, true);
			AutoUpdate = ReadBool(key, AutoUpdateValue, true);
			DeleteHiddenFiles = ReadBool(key, DeleteHiddenValue, false);

			VoidEraseSettings.DeleteHiddenFiles = DeleteHiddenFiles;
			DeleteHiddenFiles = ReadBool(key, DeleteHiddenFilesValue, false);
            _english = !Turkish;
        }
        catch
        {
            Turkish = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("tr", StringComparison.OrdinalIgnoreCase);
            ConfirmBeforeErase = true;
            AutoUpdate = true;
			DeleteHiddenFiles = false;
			VoidEraseSettings.DeleteHiddenFiles = false;
            _english = !Turkish;
        }
    }

    private static bool ReadBool(RegistryKey? key, string name, bool fallback)
    {
        object? value = key?.GetValue(name);
        return value is int i ? i != 0 : fallback;
    }

   public static void SaveSettings(
    bool confirmBeforeErase,
    bool autoUpdate,
    bool deleteHiddenFiles)
{
    ConfirmBeforeErase = confirmBeforeErase;
    AutoUpdate = autoUpdate;
    DeleteHiddenFiles = deleteHiddenFiles;

    using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
        ?? throw new InvalidOperationException("Settings could not be saved.");

    key.SetValue(
        ConfirmValue,
        confirmBeforeErase ? 1 : 0,
        RegistryValueKind.DWord);

    key.SetValue(
        AutoUpdateValue,
        autoUpdate ? 1 : 0,
        RegistryValueKind.DWord);

    key.SetValue(
        DeleteHiddenFilesValue,
        deleteHiddenFiles ? 1 : 0,
        RegistryValueKind.DWord);
}

    public static void SetLanguage(bool turkish)
    {
        Turkish = turkish;
        _english = !turkish;

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
            ?? throw new InvalidOperationException("Language preference could not be saved.");

        key.SetValue(ValueName, turkish ? "tr" : "en", RegistryValueKind.String);
    }

    public static string T(string tr, string en) => Turkish ? tr : en;

    public static void UseTurkish() => SetLanguage(true);
    public static void UseEnglish() => SetLanguage(false);
}

internal static class Program
{
    internal static string AppVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";

    internal static string DisplayVersion => "v" + AppVersion;


    internal const string MenuKey = @"Software\Classes\*\shell\VoidErase";
    internal const string DirectoryMenuKey = @"Software\Classes\Directory\shell\VoidErase";
    // Eski sürümlerde kullanılan anahtarlar. Eski kurulumların da tamamen kaldırılması için tutulur.
    internal const string LegacyMenuKey = @"Software\Classes\*\shell\PermanentDestroy";
    internal const string LegacyDirectoryMenuKey = @"Software\Classes\Directory\shell\PermanentDestroy";
    private const string CommandKey = MenuKey + @"\command";
    private const string DirectoryCommandKey = DirectoryMenuKey + @"\command";
    private const int ChunkSize = 16 * 1024 * 1024;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        L.Load();

        string? file = null;
        bool install = false;
        bool uninstall = false;
		bool mediaInfo = false;
        bool devicePurgePreflight = false;
        bool devicePurgeCapabilityTest = false;
		bool hddPreflight = false;
		
        bool hddExecutionDryRun = false;
        bool benchmark = false;
        string? mediaInfoPath = null;
        int? mediaInfoDiskNumber = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--install": install = true; break;
                case "--uninstall": uninstall = true; break;
                case "--destroy":
                    if (i + 1 < args.Length) file = args[++i].Trim('"');
                    break;
                case "--media-test":
                case "--media-info":
                    mediaInfo = true;
                    if (i + 1 < args.Length &&
                        !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        mediaInfoPath = args[++i].Trim('"');
                    }
                    break;

                case "--media-test-disk":
                case "--media-info-disk":
                    mediaInfo = true;
                    if (i + 1 >= args.Length ||
                        !int.TryParse(args[++i], out int requestedDiskNumber) ||
                        requestedDiskNumber < 0)
                    {
                        throw new ArgumentException(
                            "--media-test-disk requires a non-negative physical disk number.");
                    }
                    mediaInfoDiskNumber = requestedDiskNumber;
                    break;

                case "--device-purge-test-disk":
                case "--device-purge-preflight-disk":
                    mediaInfo = true;
                    devicePurgePreflight = true;
                    if (i + 1 >= args.Length ||
                        !int.TryParse(args[++i], out int preflightDiskNumber) ||
                        preflightDiskNumber < 0)
                    {
                        throw new ArgumentException(
                            "--device-purge-test-disk requires a non-negative physical disk number.");
                    }
                    mediaInfoDiskNumber = preflightDiskNumber;
                    break;

                case "--device-purge-capability-test-disk":
                case "--nvme-capability-test-disk":
                    mediaInfo = true;
                    devicePurgeCapabilityTest = true;
                    if (i + 1 >= args.Length ||
                        !int.TryParse(args[++i], out int capabilityDiskNumber) ||
                        capabilityDiskNumber < 0)
                    {
                        throw new ArgumentException(
                            "--device-purge-capability-test-disk requires a non-negative physical disk number.");
                    }
                    mediaInfoDiskNumber = capabilityDiskNumber;
                    break;
case "--usb-preflight":
    if (i + 1 < args.Length &&
        !args[i + 1].StartsWith("--", StringComparison.Ordinal))
    {
        mediaInfoPath = args[++i].Trim('"');
    }
    else
    {
        throw new ArgumentException(
            "--usb-preflight requires a drive path such as E:\\");
    }
    break;

case "--hdd-preflight":
    hddPreflight = true;

    if (i + 1 < args.Length &&
        !args[i + 1].StartsWith("--", StringComparison.Ordinal))
    {
        mediaInfoPath = args[++i].Trim('"');
    }
    else
    {
        throw new ArgumentException(
            "--hdd-preflight requires a drive path such as D:\\");
    }
                    break;

                case "--hdd-execution-dryrun":
                    hddExecutionDryRun = true;

                    if (i + 1 < args.Length &&
                        !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        mediaInfoPath = args[++i].Trim('"');
                    }
                    else
                    {
                        throw new ArgumentException(
                            "--hdd-execution-dryrun requires a drive path such as D:\\");
                    }
                    break;

                case "--benchmark":
                    benchmark = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        mediaInfoPath = args[++i].Trim('"');
                    else
                        throw new ArgumentException("--benchmark requires a directory path.");
                    break;

                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal))
                        file ??= args[i].Trim('"');
                    break;
            }
        }

        try
        {
			            if (benchmark)
            {
                string benchmarkDirectory = string.IsNullOrWhiteSpace(mediaInfoPath)
                    ? string.Empty
                    : Path.GetFullPath(mediaInfoPath);

                if (!Directory.Exists(benchmarkDirectory))
                {
                    MessageBox.Show(
                        L.T(
                            "Benchmark klasörü bulunamadı. Önce mevcut bir test klasörü oluşturun ve komutu bu klasörün tam yolu ile çalıştırın.\n\nÖrnek: VoidErase.exe --benchmark \\\"C:\\\\VoidErase-Test\\\"",
                            "The benchmark directory was not found. Create an existing test folder first and run the command with its full path.\n\nExample: VoidErase.exe --benchmark \\\"C:\\\\VoidErase-Test\\\""),
                        L.T("Benchmark yolu geçersiz", "Invalid benchmark path"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string output = PerformanceBenchmark.Run(benchmarkDirectory);
                Console.WriteLine("Benchmark tamamlandı: " + output);
                return;
            }

            if (hddPreflight)

{
    try
    {
        string target = Path.GetFullPath(mediaInfoPath);

        SanitizationPlan plan =
            StorageSanitizationProtocol.AnalyzePath(target);

        if (string.IsNullOrWhiteSpace(plan.DiskNumber))
        {
            MessageBox.Show(
                "HDD preflight BLOCKED.\r\n\r\n" +
                "Target could not be mapped to a physical disk.\r\n" +
                "No write operation was performed.",
                "VoidErase HDD Preflight",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        int diskNumber;

        if (!int.TryParse(plan.DiskNumber, out diskNumber))
        {
            MessageBox.Show(
                "HDD preflight BLOCKED.\r\n\r\n" +
                "Invalid physical disk number: " +
                plan.DiskNumber +
                "\r\n\r\nNo write operation was performed.",
                "VoidErase HDD Preflight",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        HddLogicalClearPreflightResult preflight =
            HddLogicalClearPreflight.AnalyzeDisk(diskNumber);

        string status =
            preflight.State == HddPreflightState.Pass
                ? "PASS"
                : preflight.State == HddPreflightState.Blocked
                    ? "BLOCKED"
                    : "ERROR";

        string message =
            "=== VoidErase HDD LogicalClear Preflight ===\r\n\r\n" +
            "Target: " + target + "\r\n" +
            "Physical disk: " + preflight.PhysicalDrive + "\r\n" +
            "Disk number: " + preflight.DiskNumber + "\r\n" +
            "Model: " + (preflight.Model ?? "(unknown)") + "\r\n" +
            "Serial: " + (preflight.SerialNumber ?? "(unknown)") + "\r\n" +
            "Bus: " + (preflight.BusType ?? "(unknown)") + "\r\n" +
            "Media: " + (preflight.MediaType ?? "(unknown)") + "\r\n" +
            "Disk size: " +
                preflight.DiskSizeBytes.ToString("N0") +
                " bytes\r\n" +
            "Logical sector: " +
                preflight.LogicalSectorSize +
                "\r\n" +
            "Physical sector: " +
                preflight.PhysicalSectorSize +
                "\r\n" +
            "System disk: " +
                (preflight.IsSystem ? "Yes" : "No") +
                "\r\n" +
            "Boot disk: " +
                (preflight.IsBoot ? "Yes" : "No") +
                "\r\n" +
            "Offline: " +
                (preflight.IsOffline ? "Yes" : "No") +
                "\r\n" +
            "Read-only: " +
                (preflight.IsReadOnly ? "Yes" : "No") +
                "\r\n\r\n" +
            "=== Safety Gate ===\r\n\r\n" +
            "Result: " + status + "\r\n\r\n" +
            "Scope:\r\n" +
            preflight.Scope +
            "\r\n\r\n" +
            "Reason:\r\n" +
            preflight.Reason +
            "\r\n\r\n" +
            "DRY RUN ONLY — no erase, overwrite, sanitize, format, " +
            "TRIM, IOCTL or device command was executed.";

        MessageBox.Show(
            message,
            "VoidErase HDD Preflight",
            MessageBoxButtons.OK,
            preflight.State == HddPreflightState.Pass
                ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning);
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            "HDD preflight failed:\r\n\r\n" + ex,
            "VoidErase HDD Preflight",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    return;
}
            if (hddExecutionDryRun)
            {
                try
                {
                    string target = Path.GetFullPath(mediaInfoPath ?? "");

                    SanitizationPlan plan =
                        StorageSanitizationProtocol.AnalyzePath(target);

                    if (string.IsNullOrWhiteSpace(plan.DiskNumber))
                    {
                        MessageBox.Show(
                            "HDD execution DRY-RUN BLOCKED.\r\n\r\n" +
                            "Target could not be mapped to a physical disk.\r\n" +
                            "No write operation was performed.",
                            "VoidErase HDD Execution Gate",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    int diskNumber;
                    if (!int.TryParse(plan.DiskNumber, out diskNumber))
                    {
                        MessageBox.Show(
                            "HDD execution DRY-RUN BLOCKED.\r\n\r\n" +
                            "Invalid physical disk number: " + plan.DiskNumber +
                            "\r\n\r\nNo write operation was performed.",
                            "VoidErase HDD Execution Gate",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    HddLogicalClearPreflightResult preflight =
                        HddLogicalClearPreflight.AnalyzeDisk(diskNumber);

                    if (preflight.State != HddPreflightState.Pass)
                    {
                        MessageBox.Show(
                            "HDD execution DRY-RUN BLOCKED.\r\n\r\n" +
                            "Preflight: " + preflight.State + "\r\n" +
                            "Reason: " + preflight.Reason +
                            "\r\n\r\nNo write operation was performed.",
                            "VoidErase HDD Execution Gate",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    HddLogicalClearExecutionPlan executionPlan =
                        HddLogicalClearExecution.Prepare(
                            target,
                            diskNumber,
                            preflight.Model,
                            preflight.SerialNumber,
                            preflight.DiskSizeBytes);

                    string executionStatus =
                        executionPlan.State.ToString().ToUpperInvariant();

                    string gateStatus =
                        executionPlan.Gate == null
                            ? "UNKNOWN"
                            : executionPlan.Gate.State.ToString().ToUpperInvariant();

                    string executionMessage =
                        "=== VoidErase HDD LogicalClear Execution Gate ===\r\n\r\n" +
                        "Target: " + target + "\r\n" +
                        "Physical disk: " +
                            (executionPlan.Gate?.PhysicalDrive ?? preflight.PhysicalDrive) +
                            "\r\n" +
                        "Disk number: " +
                            (executionPlan.Gate?.DiskNumber.ToString() ?? diskNumber.ToString()) +
                            "\r\n" +
                        "Model: " +
                            (executionPlan.Gate?.Model ?? preflight.Model ?? "(unknown)") +
                            "\r\n" +
                        "Serial: " +
                            (executionPlan.Gate?.SerialNumber ?? preflight.SerialNumber ?? "(unknown)") +
                            "\r\n" +
                        "Media: " +
                            (executionPlan.Gate?.MediaType ?? preflight.MediaType ?? "(unknown)") +
                            "\r\n" +
                        "System disk: " +
                            ((executionPlan.Gate?.IsSystem ?? preflight.IsSystem) ? "Yes" : "No") +
                            "\r\n" +
                        "Boot disk: " +
                            ((executionPlan.Gate?.IsBoot ?? preflight.IsBoot) ? "Yes" : "No") +
                            "\r\n" +
                        "Offline: " +
                            ((executionPlan.Gate?.IsOffline ?? preflight.IsOffline) ? "Yes" : "No") +
                            "\r\n" +
                        "Read-only: " +
                            ((executionPlan.Gate?.IsReadOnly ?? preflight.IsReadOnly) ? "Yes" : "No") +
                            "\r\n\r\n" +
                        "Preflight: PASS\r\n" +
                        "Final execution gate: " + gateStatus + "\r\n" +
                        "Execution plan: " + executionStatus + "\r\n" +
                        "Logical sector: " + executionPlan.LogicalSectorSize + " bytes\r\n" +
                        "Physical sector: " + executionPlan.PhysicalSectorSize + " bytes\r\n" +
                        "Addressable bytes: " + executionPlan.AddressableBytes.ToString("N0") + "\r\n" +
                        "Sector-aligned bytes: " + executionPlan.AlignedWriteBytes.ToString("N0") + "\r\n" +
                        "Planning block: " + executionPlan.BlockSizeBytes.ToString("N0") + " bytes\r\n" +
                        "Planned blocks: " + executionPlan.BlockCount.ToString("N0") + "\r\n" +
                        "Execution mode: " + (executionPlan.ExecutionMode ?? "(unknown)") + "\r\n" +
                        "Verification plan: " + (executionPlan.VerificationPlan ?? "(unknown)") + "\r\n\r\n" +
                        "Message:\r\n" +
                        executionPlan.Message +
                        "\r\n\r\n" +
                        "DRY RUN ONLY — no erase, overwrite, sanitize, format, " +
                        "TRIM, IOCTL or device command was executed.";

                    MessageBox.Show(
                        executionMessage,
                        "VoidErase HDD Execution Gate",
                        MessageBoxButtons.OK,
                        executionPlan.State == HddLogicalClearExecutionState.Ready
                            ? MessageBoxIcon.Information
                            : MessageBoxIcon.Warning);

                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "HDD execution DRY-RUN failed:\r\n\r\n" + ex,
                        "VoidErase HDD Execution Gate",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return;
            }

            if (mediaInfo)
            {
                SanitizationPlan plan;
                string target;

                if (mediaInfoDiskNumber.HasValue)
                {
                    plan = StorageSanitizationProtocol.AnalyzePhysicalDiskNumber(
                        mediaInfoDiskNumber.Value);
                    target = plan.PhysicalDrive;
                }
                else
                {
                    target = string.IsNullOrWhiteSpace(mediaInfoPath)
                        ? Path.GetPathRoot(Environment.SystemDirectory) ?? Environment.SystemDirectory
                        : Path.GetFullPath(mediaInfoPath);

                    plan = StorageSanitizationProtocol.AnalyzePath(target);
                }

                if (devicePurgeCapabilityTest)
                {
                    NvmeSanitizeCapabilityResult capability;

                    if (plan.MediaKind != StorageMediaKind.Nvme)
                    {
                        capability = new NvmeSanitizeCapabilityResult
                        {
                            Status = NvmeSanitizeCapabilityStatus.NotApplicable,
                            PreferredMethod = "Not applicable — target is not classified as NVMe",
                            Detail = "The capability probe is intentionally limited to NVMe media."
                        };
                    }
                    else if (plan.IsSystemDisk)
                    {
                        capability = new NvmeSanitizeCapabilityResult
                        {
                            Status = NvmeSanitizeCapabilityStatus.Unknown,
                            PreferredMethod = "BLOCKED — running Windows disk",
                            Detail = "Capability probing is blocked for the running Windows system disk. No device command was issued."
                        };
                    }
                    else
                    {
                        capability = NvmeCapabilityProbe.Probe(plan.PhysicalDrive);
                    }

                    string capabilityStatus = capability.Status.ToString().ToUpperInvariant();

                    string capabilityResult =
                        "=== VoidErase NVMe Sanitize Capability Probe (READ-ONLY) ===\n\n" +
                        "Physical disk: " + (plan.PhysicalDrive ?? "(unknown)") + "\n" +
                        "Disk number: " + (plan.DiskNumber ?? "(unknown)") + "\n" +
                        "Model: " + (plan.Model ?? "(unknown)") + "\n" +
                        "Media: " + plan.MediaKind + "\n" +
                        "Bus: " + (plan.BusType ?? "(unknown)") + "\n" +
                        "System disk: " + (plan.IsSystemDisk ? "Yes" : "No") + "\n\n" +
                        "=== Capability ===\n\n" +
                        "Status: " + capabilityStatus + "\n" +
                        "Preferred method: " + capability.PreferredMethod + "\n" +
                        "Crypto Erase: " + (capability.CryptoErase ? "Yes" : "No") + "\n" +
                        "Block Erase: " + (capability.BlockErase ? "Yes" : "No") + "\n" +
                        "Overwrite: " + (capability.Overwrite ? "Yes" : "No") + "\n" +
                        "SANICAP: 0x" + capability.SanitizeCapabilitiesRaw.ToString("X8") + "\n" +
                        "NDI: " + (capability.NoDeallocateInhibited ? "Yes" : "No") + "\n" +
                        "NODMMAS modifies media: " + (capability.NoDeallocateAfterSanitizeModifiesMedia ? "Yes" : "No") + "\n\n" +
                        "Detail: " + capability.Detail + "\n\n" +
                        "Execution: BLOCKED — capability probe only. No sanitize, erase, format, or write command is executed.";

                    MessageBox.Show(
                        capabilityResult,
                        "VoidErase NVMe Capability Probe",
                        MessageBoxButtons.OK,
                        capability.Status == NvmeSanitizeCapabilityStatus.Supported
                            ? MessageBoxIcon.Information
                            : MessageBoxIcon.Warning);

                    return;
                }

                if (devicePurgePreflight)
                {
                    bool physicalDiskKnown =
                        !string.IsNullOrWhiteSpace(plan.PhysicalDrive);

                    bool diskNumberKnown =
                        !string.IsNullOrWhiteSpace(plan.DiskNumber);

                    bool nonSystemDisk = !plan.IsSystemDisk;

                    bool deviceLevelDecision =
                        plan.DeviceCommandRequired &&
                        (plan.RecommendedStrength == SanitizationStrength.DevicePurge ||
                         plan.RecommendedStrength == SanitizationStrength.CryptographicErase);

                    bool supportedMediaForPreflight =
                        plan.MediaKind == StorageMediaKind.Nvme ||
                        plan.MediaKind == StorageMediaKind.SataSsd ||
                        plan.MediaKind == StorageMediaKind.UsbFlash;

                    bool safetyGate =
                        physicalDiskKnown &&
                        diskNumberKnown &&
                        nonSystemDisk &&
                        deviceLevelDecision &&
                        supportedMediaForPreflight;

                    string capability =
                        "UNKNOWN — this dry-run does not issue a sanitize command " +
                        "and does not claim device capability support.";

                    string execution =
                        "BLOCKED — preflight only; no device command will be executed.";

                    string preflightResult =
                        "=== VoidErase Device Purge Preflight (DRY RUN) ===\n\n" +
                        "Physical disk: " + (physicalDiskKnown ? plan.PhysicalDrive : "(unknown)") + "\n" +
                        "Disk number: " + (diskNumberKnown ? plan.DiskNumber : "(unknown)") + "\n" +
                        "Model: " + (plan.Model ?? "(unknown)") + "\n" +
                        "Media: " + plan.MediaKind + "\n" +
                        "Bus: " + (plan.BusType ?? "(unknown)") + "\n" +
                        "System disk: " + (plan.IsSystemDisk ? "Yes" : "No") + "\n" +
                        "Recommended: " + plan.RecommendedStrength + "\n" +
                        "Method: " + (plan.RecommendedMethod ?? "(unknown)") + "\n\n" +
                        "=== Safety Gates ===\n\n" +
                        "Physical disk identified: " + (physicalDiskKnown ? "PASS" : "FAIL") + "\n" +
                        "Disk number identified: " + (diskNumberKnown ? "PASS" : "FAIL") + "\n" +
                        "System-disk protection: " + (nonSystemDisk ? "PASS" : "FAIL — system disk") + "\n" +
                        "Device-level decision: " + (deviceLevelDecision ? "PASS" : "FAIL") + "\n" +
                        "Supported media class for preflight: " + (supportedMediaForPreflight ? "PASS" : "FAIL") + "\n\n" +
                        "Overall safety gate: " + (safetyGate ? "PASS — eligible for a future device-level execution stage" : "BLOCKED") + "\n" +
                        "Capability: " + capability + "\n" +
                        "Execution: " + execution + "\n\n" +
                        "No erase, sanitize, secure-erase, purge, or device command is executed.";

                    MessageBox.Show(
                        preflightResult,
                        "VoidErase Device Purge Preflight",
                        MessageBoxButtons.OK,
                        safetyGate ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                    return;
                }

                string result =
                    "=== VoidErase Storage Sanitization V2 ===\n\n" +
                    "Path: " + target + "\n" +
                    "Drive: " + plan.DriveRoot + "\n" +
                    "Media: " + plan.MediaKind + "\n" +
                    "Model: " + (plan.Model ?? "(unknown)") + "\n" +
                    "Bus: " + (plan.BusType ?? "(unknown)") + "\n" +
                    "Serial: " + (string.IsNullOrWhiteSpace(plan.SerialNumber) ? "(unavailable)" : plan.SerialNumber) + "\n" +
                    "Physical disk: " + (string.IsNullOrWhiteSpace(plan.PhysicalDrive) ? "(unknown)" : plan.PhysicalDrive) + "\n" +
                    "Disk number: " + (string.IsNullOrWhiteSpace(plan.DiskNumber) ? "(unknown)" : plan.DiskNumber) + "\n" +
                    "Windows media type: " + (string.IsNullOrWhiteSpace(plan.WindowsMediaType) ? "(unknown)" : plan.WindowsMediaType) + "\n" +
                    "Encrypted: " + (plan.Encrypted ? "Yes" : "No") + "\n" +
                    "BitLocker status: " + (string.IsNullOrWhiteSpace(plan.EncryptionStatus) ? "(unknown)" : plan.EncryptionStatus) + "\n" +
                    "System disk: " + (plan.IsSystemDisk ? "Yes" : "No") + "\n\n" +
                    "=== Sanitization Decision ===\n\n" +
                    "Recommended: " + plan.RecommendedStrength + "\n" +
                    "Method: " + plan.RecommendedMethod + "\n" +
                    "Device command required: " + (plan.DeviceCommandRequired ? "Yes" : "No") + "\n\n" +
                    "Reason:\n" + plan.Reason + "\n\n" +
                    "This test only detects media/capabilities. No erase or device sanitization command is executed.";

                MessageBox.Show(
                    result,
                    "VoidErase Storage Sanitization V2",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            if (install)
            {
                bool ok = InstallContextMenu(false);
                MessageBox.Show(
                    L.T("Sağ tık menüsü başarıyla etkinleştirildi.", "Context menu enabled successfully."),
                    "VoidErase",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }

            if (uninstall)
            {
                bool ok = UninstallContextMenu();
                MessageBox.Show(
                    L.T("Sağ tık menüsü kaldırıldı.", "Context menu removed."),
                    "VoidErase",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }

            // Explorer sağ tık çağrısı: ana arayüzü açma.
            // --destroy parametresi yalnızca onay + işlem modunu başlatır.
            if (file != null && args.Any(a =>
                a.Equals("--destroy", StringComparison.OrdinalIgnoreCase)))
            {
                Application.Run(new ShellDestroyForm(file));
                return;
            }

            Application.Run(new MainForm(null));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "VoidErase",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static string GetExePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException(L.T("Çalışan EXE yolu alınamadı.", "The executable path could not be determined."));
    }

    internal static bool InstallContextMenu(bool showMessage)
    {
        string exe = GetExePath();
        string command = $"\"{exe}\" --destroy \"%1\"";

        // Eski ve yeni kayıtların çakışmasını önle.
        Registry.CurrentUser.DeleteSubKeyTree(LegacyMenuKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyDirectoryMenuKey, false);

        CreateContextMenuEntry(MenuKey, CommandKey, exe, command);
        CreateContextMenuEntry(DirectoryMenuKey, DirectoryCommandKey, exe, command);

        bool fileOk = VerifyContextCommand(CommandKey, command);
        bool directoryOk = VerifyContextCommand(DirectoryCommandKey, command);

        ShellRefresh.Notify();
        return fileOk && directoryOk;
    }

    private static void CreateContextMenuEntry(string menuKey, string commandKey, string exe, string command)
    {
        using (RegistryKey menu = Registry.CurrentUser.CreateSubKey(menuKey, true)
            ?? throw new InvalidOperationException(L.T("Registry menü anahtarı oluşturulamadı.", "The Registry menu key could not be created.")))
        {
            menu.SetValue("", L.T("Kalıcı Olarak Yok Et", "Permanent Delete"), RegistryValueKind.String);
            menu.SetValue("Icon", exe, RegistryValueKind.String);
            menu.SetValue("Position", "Bottom", RegistryValueKind.String);
        }

        using (RegistryKey cmd = Registry.CurrentUser.CreateSubKey(commandKey, true)
            ?? throw new InvalidOperationException(L.T("Registry command anahtarı oluşturulamadı.", "The Registry command key could not be created.")))
        {
            cmd.SetValue("", command, RegistryValueKind.String);
        }
    }

    private static bool VerifyContextCommand(string commandKey, string expected)
    {
        using RegistryKey? verify = Registry.CurrentUser.OpenSubKey(commandKey);
        return string.Equals(verify?.GetValue("") as string, expected, StringComparison.Ordinal);
    }

    internal static void UpdateContextMenuLanguage()
    {
        foreach (string keyName in new[] { MenuKey, DirectoryMenuKey })
        {
            using RegistryKey? menu = Registry.CurrentUser.OpenSubKey(keyName, writable: true);
            menu?.SetValue("", L.T("Kalıcı Olarak Yok Et", "Permanent Delete"), RegistryValueKind.String);
        }
        ShellRefresh.Notify();
    }

    internal static bool UninstallContextMenu()
    {
        // Güncel ve eski sürümlerin hem dosya hem klasör kayıtlarını kaldır.
        Registry.CurrentUser.DeleteSubKeyTree(MenuKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(DirectoryMenuKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyMenuKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyDirectoryMenuKey, false);
        ShellRefresh.Notify();

        return Registry.CurrentUser.OpenSubKey(MenuKey) == null
            && Registry.CurrentUser.OpenSubKey(DirectoryMenuKey) == null
            && Registry.CurrentUser.OpenSubKey(LegacyMenuKey) == null
            && Registry.CurrentUser.OpenSubKey(LegacyDirectoryMenuKey) == null;
    }


    internal static void DestroyFileSilent(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(L.T("Dosya bulunamadı.", "File not found."), path);

        FileInfo info = new(path);
        if ((info.Attributes & FileAttributes.System) != 0)
            throw new InvalidOperationException(L.T("Sistem dosyaları üzerinde işlem yapılmıyor.", "System files are not processed."));
if ((info.Attributes & FileAttributes.Hidden) != 0)
{
    info.Attributes &= ~FileAttributes.Hidden;
}

if ((info.Attributes & FileAttributes.ReadOnly) != 0)
{
    info.Attributes &= ~FileAttributes.ReadOnly;
}
        string temp = Path.Combine(info.DirectoryName!,
            "." + info.Name + "." + Guid.NewGuid().ToString("N") + ".destroying");

        byte[] key = CryptoCompat.RandomBytes(32);
        byte[] headerNonce = CryptoCompat.RandomBytes(12);

        try
        {
            EncryptChunksSilent(path, temp, key, headerNonce);
            ValidateContainerSilent(temp, key, headerNonce);
            File.Delete(path);
            File.Delete(temp);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        finally
        {
			// NIST SP 800-88 Rev. 2 uyarlaması:
			// İşlem anahtarı kullanım sonrasında bellekten sıfırlanır.
            CryptoCompat.ZeroMemory(key);
            CryptoCompat.ZeroMemory(headerNonce);
        }
    }

    private static void EncryptChunksSilent(
        string source, string destination, byte[] key, byte[] headerNonce)
    {
        FileInfo info = new(source);
        long total = info.Length;
        long chunks = total == 0 ? 0 : (total + ChunkSize - 1) / ChunkSize;

        using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 1024 * 1024, FileOptions.SequentialScan);

        CryptoCompat.WriteAll(output, new byte[] { (byte)'P', (byte)'D', (byte)'S', (byte)'1' });
        output.WriteByte(1);
        byte[] intMetadata = new byte[4];
        byte[] longMetadata = new byte[8];
        WriteInt32(intMetadata, ChunkSize);
        CryptoCompat.WriteAll(output, intMetadata);
        WriteInt64(longMetadata, total);
        CryptoCompat.WriteAll(output, longMetadata);
        WriteInt64(longMetadata, chunks);
        CryptoCompat.WriteAll(output, longMetadata);
        CryptoCompat.WriteAll(output, headerNonce);

        using SecureRentedBuffer plainLease = SecureRentedBuffer.Rent(ChunkSize);
        using SecureRentedBuffer cipherLease = SecureRentedBuffer.Rent(ChunkSize);
        byte[] plain = plainLease.Buffer;
        byte[] cipher = cipherLease.Buffer;
        byte[] tag = new byte[16];
        byte[] nonce = new byte[12];
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        using AesGcmCompat aes = new AesGcmCompat(key);
        long index = 0;

        while (true)
        {
            int read = ReadChunk(input, plain);
            if (read == 0) break;

            MakeNonce(headerNonce, index, nonce);
            sourceHash.AppendData(plain, 0, read);

            aes.Encrypt(nonce, plain, 0, read,
                cipher, 0, tag);

            WriteInt32(intMetadata, read);
            CryptoCompat.WriteAll(output, intMetadata);
            CryptoCompat.WriteAll(output, nonce);
            CryptoCompat.WriteAll(output, tag);
            output.Write(cipher, 0, read);

            CryptoCompat.ZeroMemory(nonce);
            CryptoCompat.ZeroMemory(tag);
            index++;
        }

        byte[] sourceDigest = sourceHash.GetHashAndReset();
        CryptoCompat.WriteAll(output, sourceDigest);
        CryptoCompat.ZeroMemory(sourceDigest);
        output.Flush(true);
        CryptoCompat.ZeroMemory(plain);
        CryptoCompat.ZeroMemory(cipher);
        CryptoCompat.ZeroMemory(tag);
    }

    private static void ValidateContainerSilent(
        string path, byte[] key, byte[] expectedHeaderNonce)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);

        byte[] magic = new byte[4];
        ReadExactly(fs, magic);
        if (magic[0] != 'P' || magic[1] != 'D' || magic[2] != 'S' || magic[3] != '1')
            throw new InvalidDataException(L.T("Container başlığı geçersiz.", "Invalid container header."));

        int version = fs.ReadByte();
        if (version != 1) throw new InvalidDataException(L.T("Container sürümü geçersiz.", "Invalid container version."));

        byte[] b4 = new byte[4];
        byte[] b8 = new byte[8];

        ReadExactly(fs, b4);
        int chunkSize = BitConverter.ToInt32(b4, 0);
        ReadExactly(fs, b8);
        long total = BitConverter.ToInt64(b8, 0);
        ReadExactly(fs, b8);
        long chunks = BitConverter.ToInt64(b8, 0);

        byte[] headerNonce = new byte[12];
        ReadExactly(fs, headerNonce);

        if (!CryptoCompat.FixedTimeEquals(headerNonce, expectedHeaderNonce))
            throw new CryptographicException(L.T("Nonce doğrulaması başarısız.", "Nonce validation failed."));

        if (chunkSize != ChunkSize || total < 0 || chunks < 0)
            throw new InvalidDataException(L.T("Container bilgileri geçersiz.", "Invalid container information."));

        using SecureRentedBuffer cipherLease = SecureRentedBuffer.Rent(ChunkSize);
        using SecureRentedBuffer plainLease = SecureRentedBuffer.Rent(ChunkSize);
        byte[] cipher = cipherLease.Buffer;
        byte[] plain = plainLease.Buffer;
        byte[] tag = new byte[16];

        using AesGcmCompat aes = new AesGcmCompat(key);
        using IncrementalHash plainHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long counted = 0;

        for (long i = 0; i < chunks; i++)
        {
            ReadExactly(fs, b4);
            int length = BitConverter.ToInt32(b4, 0);
            if (length < 0 || length > ChunkSize)
                throw new InvalidDataException(L.T("Chunk uzunluğu geçersiz.", "Invalid chunk length."));

            byte[] nonce = new byte[12];
            ReadExactly(fs, nonce);
            ReadExactly(fs, tag);
            ReadExactly(fs, cipher, 0, length);

            aes.Decrypt(nonce, cipher, 0, length,
                tag, plain, 0);

            plainHash.AppendData(plain, 0, length);
            counted += length;

            CryptoCompat.ZeroMemory(nonce);
            CryptoCompat.ZeroMemory(tag);
        }

        byte[] expectedDigest = new byte[32];
        ReadExactly(fs, expectedDigest);
        byte[] actualDigest = plainHash.GetHashAndReset();

        if (counted != total || fs.Position != fs.Length ||
            !CryptoCompat.FixedTimeEquals(expectedDigest, actualDigest))
            throw new InvalidDataException(L.T("Container doğrulaması başarısız.", "Container validation failed."));

        CryptoCompat.ZeroMemory(expectedDigest);
        CryptoCompat.ZeroMemory(actualDigest);
        CryptoCompat.ZeroMemory(cipher);
        CryptoCompat.ZeroMemory(plain);
        CryptoCompat.ZeroMemory(tag);
    }

private static void EnsurePathAllowed(string path)
{
    string fullPath;

    try
    {
        fullPath = Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }
    catch
    {
        throw new InvalidOperationException(
            L.T(
                "Hedef yol geçerli değil.",
                "The target path is invalid."));
    }

    // Kullanıcının açıkça korumaya aldığı yollar.
    if (VoidEraseSettings.IsProtectedPath(fullPath))
    {
        throw new InvalidOperationException(
            L.T(
                "Bu yol kullanıcı tarafından korumalı olarak işaretlenmiş:\n\n" + fullPath,
                "This path is protected by the user:\n\n" + fullPath));
    }

    // C:\, D:\ vb. sürücü köklerini koru.
    if (VoidEraseSettings.ProtectSystemDrive &&
        Path.GetPathRoot(fullPath) != null &&
        string.Equals(
            fullPath.TrimEnd('\\'),
            Path.GetPathRoot(fullPath)!.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            L.T(
                "Sürücü kökü güvenlik nedeniyle korunuyor:\n\n" + fullPath,
                "Drive roots are protected for safety:\n\n" + fullPath));
    }

    if (!VoidEraseSettings.ProtectSystemPaths)
        return;

    string windows = Environment.GetFolderPath(
        Environment.SpecialFolder.Windows);

    string programFiles = Environment.GetFolderPath(
        Environment.SpecialFolder.ProgramFiles);

    string programFilesX86 = Environment.GetFolderPath(
        Environment.SpecialFolder.ProgramFilesX86);

    string programData = Environment.GetFolderPath(
        Environment.SpecialFolder.CommonApplicationData);

    string[] protectedSystemPaths =
    {
        windows,
        programFiles,
        programFilesX86,
        programData
    };

    foreach (string protectedPath in protectedSystemPaths)
    {
        if (string.IsNullOrWhiteSpace(protectedPath))
            continue;

        string normalized = Path.GetFullPath(protectedPath)
            .TrimEnd('\\');

        if (fullPath.Equals(
                normalized,
                StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(
                normalized + "\\",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                L.T(
                    "Windows sistem yolu güvenlik nedeniyle korunuyor:\n\n" + fullPath,
                    "This Windows system path is protected for safety:\n\n" + fullPath));
        }
    }
}

internal static int DestroyPath(
    string path,
    IProgressReporter form,
    out List<string> skippedFiles)
{
	skippedFiles = new List<string>();
    form.ThrowIfCancellationRequested();

    EnsurePathAllowed(path);

    if (File.Exists(path))
    {
        FileAttributes attributes = File.GetAttributes(path);

        // Reparse point olan dosyalara da takip etmeden doğrudan müdahale etmiyoruz.
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                L.T(
                    "Sembolik bağlantı veya reparse point olan öğeler üzerinde işlem yapılmıyor.",
                    "Symbolic links and reparse-point items are not processed."));
        }

        DestroyFile(path, form);
		return 1;
    }

    if (Directory.Exists(path))
    {
        DirectoryInfo directoryInfo = new DirectoryInfo(path);

        // Junction / symbolic link / diğer reparse point klasörleri takip edilmez.
        if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                L.T(
                    "Sembolik bağlantı veya junction olan klasörler üzerinde işlem yapılmıyor.",
                    "Symbolic-link or junction directories are not processed."));
        }

int verifiedFiles;
List<string> directorySkippedFiles;

DestroyDirectory(
    path,
    form,
    out verifiedFiles,
    out directorySkippedFiles);

if (directorySkippedFiles.Count > 0)
{
    skippedFiles.AddRange(directorySkippedFiles);
}

return verifiedFiles;
}

    throw new FileNotFoundException(
        L.T("Dosya veya klasör bulunamadı.", "File or folder not found."),
        path);
}

private static void DestroyDirectory(
    string directory,
    IProgressReporter form,
    out int verifiedFiles,
    out List<string> skippedFiles)
{
	verifiedFiles = 0;
	skippedFiles = new List<string>();
    form.ThrowIfCancellationRequested();

    DirectoryInfo root = new DirectoryInfo(directory);

    if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
    {
        throw new InvalidOperationException(
            L.T(
                "Sembolik bağlantı veya junction olan klasörler üzerinde işlem yapılmıyor.",
                "Symbolic-link or junction directories are not processed."));
    }

    List<string> files = new List<string>();
    List<string> directories = new List<string>();

    // Önce ağacı kendimiz dolaşıyoruz.
    // Directory.EnumerateFiles(..., AllDirectories) yerine explicit traversal
    // kullanarak reparse point'leri takip etmiyoruz.
    Stack<string> pending = new Stack<string>();
    pending.Push(directory);

    while (pending.Count > 0)
    {
        form.ThrowIfCancellationRequested();

        string current = pending.Pop();

        string[] entries;

        try
        {
            entries = Directory.GetFileSystemEntries(current);
        }
        catch (UnauthorizedAccessException ex)
{
    throw new UnauthorizedAccessException(
        L.T(
            "Klasöre erişim izni yok:\n" + current,
            "Access denied:\n" + current),
        ex);
}
        catch (IOException ex)
        {
            throw new IOException(
                L.T(
                    "Klasör okunamadı:\n" + current,
                    "Directory could not be read:\n" + current),
                ex);
        }

        foreach (string entry in entries)
        {
            form.ThrowIfCancellationRequested();

            FileAttributes attributes;

            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                throw new IOException(
                    L.T(
                        "Dosya veya klasör özellikleri okunamadı:\n" + entry,
                        "File or directory attributes could not be read:\n" + entry),
                    ex);
            }

            // Junction, symbolic link veya başka bir reparse point:
            // içine kesinlikle girmiyoruz.
            if ((attributes & FileAttributes.ReparsePoint) != 0)
{
    if (VoidEraseSettings.SkipReparsePoints)
    {
        continue;
    }

    throw new InvalidOperationException(
        L.T(
            "Sembolik bağlantı veya reparse point olan öğeler üzerinde işlem yapılmıyor:\n" + entry,
            "Symbolic links and reparse-point items are not processed:\n" + entry));
}

            if ((attributes & FileAttributes.Directory) != 0)
{
    directories.Add(entry);
    pending.Push(entry);
}
else
{
    if ((attributes & FileAttributes.Hidden) != 0 &&
        !VoidEraseSettings.DeleteHiddenFiles)
    {
        skippedFiles.Add(
            entry + " — " +
            L.T(
                "Gizli dosya ayarlarda korunuyor.",
                "Hidden file is protected by the current setting."));
        continue;
    }

    files.Add(entry);
}
        }
    }

    long totalBytes = 0;

    foreach (string file in files)
    {
        form.ThrowIfCancellationRequested();

        try
        {
            FileInfo info = new FileInfo(file);

            if ((info.Attributes & FileAttributes.System) != 0)
{
    throw new InvalidOperationException(
        L.T(
            "Sistem dosyaları güvenlik nedeniyle işlenmiyor:\n" + file,
            "System files are skipped for safety:\n" + file));
}



if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
{
    throw new InvalidOperationException(
        L.T(
            "Sembolik bağlantı veya reparse point olan dosya işlenmiyor:\n" + file,
            "Symbolic-link or reparse-point files are not processed:\n" + file));
}

            totalBytes += info.Length;
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (IOException)
        {
            throw;
        }
    }

    if (files.Count == 0)
    {
        // Boş klasör.
        if (Directory.Exists(directory))
            Directory.Delete(directory, false);

        return;
    }

    long completedBytes = 0;
    Stopwatch overall = Stopwatch.StartNew();
	

    foreach (string file in files)
    {
        form.ThrowIfCancellationRequested();

        long fileSize;

        try
        {
            fileSize = new FileInfo(file).Length;
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException)
        {
            throw new IOException(
                L.T(
                    "Dosya boyutu okunamadı:\n" + file,
                    "File size could not be read:\n" + file),
                ex);
        }

        form.ReportProgress(
            completedBytes,
            Math.Max(totalBytes, 1),
            overall.Elapsed);

       DestroyFile(
		file,
		new OffsetProgressReporter(
			form,
			completedBytes,
			fileSize,
			totalBytes));

		verifiedFiles++;

	completedBytes += fileSize;

        form.ReportProgress(
            completedBytes,
            Math.Max(totalBytes, 1),
            overall.Elapsed);
    }

    // Alt klasörleri sondan başa doğru sil.
    directories
        .OrderByDescending(d => d.Length)
        .ToList()
        .ForEach(subdir =>
        {
            form.ThrowIfCancellationRequested();

            if (!Directory.Exists(subdir))
                return;

            DirectoryInfo info = new DirectoryInfo(subdir);

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    L.T(
                        "Reparse point klasör silme aşamasında tespit edildi:\n" + subdir,
                        "A reparse-point directory was detected during deletion:\n" + subdir));
            }

            if (!Directory.EnumerateFileSystemEntries(subdir).Any())
                Directory.Delete(subdir, false);
        });

    form.ThrowIfCancellationRequested();

    if (Directory.Exists(directory))
    {
        DirectoryInfo finalRoot = new DirectoryInfo(directory);

        if ((finalRoot.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                L.T(
                    "Ana klasör reparse point olarak değiştiği için silme durduruldu.",
                    "Deletion stopped because the root directory became a reparse point."));
        }

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory, false);
    }
}
    private sealed class OffsetProgressReporter : IProgressReporter
    {
        private readonly IProgressReporter inner;
        private readonly long offset;
        private readonly long fileTotal;
        private readonly long overallTotal;

        public OffsetProgressReporter(IProgressReporter inner, long offset, long fileTotal, long overallTotal)
        {
            this.inner = inner;
            this.offset = offset;
            this.fileTotal = fileTotal;
            this.overallTotal = Math.Max(overallTotal, 1);
        }

        public void ReportProgress(long processed, long total, TimeSpan elapsed)
        {
            long scaled = offset + Math.Min(processed, fileTotal);
            inner.ReportProgress(scaled, overallTotal, elapsed);
        }

        public void ReportValidation(long current, long total, TimeSpan elapsed)
        {
            inner.ReportValidation(current, total, elapsed);
        }

        public void ReportFinalizing() => inner.ReportFinalizing();
        public void ThrowIfCancellationRequested() => inner.ThrowIfCancellationRequested();
    }

    internal static void DestroyFile(string path, IProgressReporter form)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(L.T("Dosya bulunamadı.", "File not found."), path);

        FileInfo info = new(path);

        if ((info.Attributes & FileAttributes.System) != 0)
            throw new InvalidOperationException(L.T("Sistem dosyaları üzerinde işlem yapılmıyor.", "System files are not processed."));

        string temp = Path.Combine(
            info.DirectoryName!,
            "." + info.Name + "." + Guid.NewGuid().ToString("N") + ".destroying");

        byte[] key = CryptoCompat.RandomBytes(32);
byte[] headerNonce = CryptoCompat.RandomBytes(12);

try
{
    EncryptChunks(path, temp, key, headerNonce, form);
    ValidateContainer(temp, key, headerNonce, form);

    form.ThrowIfCancellationRequested();

    form.ReportFinalizing();

	form.ThrowIfCancellationRequested();
File.Delete(temp);

form.ThrowIfCancellationRequested();
File.Delete(path);

if (File.Exists(path))
    throw new IOException(
        L.T(
            "Kaynak dosya silinemedi.",
            "The source file could not be deleted."));

VerificationResult verification =
    SanitizationVerification.VerifyPathAbsent(path);

if (verification.Status != VerificationStatus.Verified)
    throw new IOException(
        L.T(
            "Kaynak dosyanın silindiği doğrulanamadı.",
            "The source file deletion could not be verified."));
}
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        finally
        {
            CryptoCompat.ZeroMemory(key);
            CryptoCompat.ZeroMemory(headerNonce);
        }
    }

    internal static void EncryptChunks(
        string source, string destination,
        byte[] key, byte[] headerNonce, IProgressReporter form)
    {
        FileInfo info = new(source);
        long total = info.Length;
        long chunks = total == 0 ? 0 : (total + ChunkSize - 1) / ChunkSize;

        using FileStream input = new(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);

        using FileStream output = new(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.SequentialScan);

        CryptoCompat.WriteAll(output, new byte[] { (byte)'P', (byte)'D', (byte)'S', (byte)'1' });
        output.WriteByte(1);
        byte[] intMetadata = new byte[4];
        byte[] longMetadata = new byte[8];
        WriteInt32(intMetadata, ChunkSize);
        CryptoCompat.WriteAll(output, intMetadata);
        WriteInt64(longMetadata, total);
        CryptoCompat.WriteAll(output, longMetadata);
        WriteInt64(longMetadata, chunks);
        CryptoCompat.WriteAll(output, longMetadata);
        CryptoCompat.WriteAll(output, headerNonce);

        using SecureRentedBuffer plainLease = SecureRentedBuffer.Rent(ChunkSize);
        using SecureRentedBuffer cipherLease = SecureRentedBuffer.Rent(ChunkSize);
        byte[] plain = plainLease.Buffer;
        byte[] cipher = cipherLease.Buffer;
        byte[] tag = new byte[16];
        byte[] nonce = new byte[12];
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        using AesGcmCompat aes = new AesGcmCompat(key);
        Stopwatch timer = Stopwatch.StartNew();
        long processed = 0;
        long index = 0;

        while (true)
        {
            form.ThrowIfCancellationRequested();

            int read = ReadChunk(input, plain);
            if (read == 0) break;

            MakeNonce(headerNonce, index, nonce);
            sourceHash.AppendData(plain, 0, read);

            aes.Encrypt(nonce,
                plain, 0, read,
                cipher, 0, tag);

            WriteInt32(intMetadata, read);
            CryptoCompat.WriteAll(output, intMetadata);
            CryptoCompat.WriteAll(output, nonce);
            CryptoCompat.WriteAll(output, tag);
            output.Write(cipher, 0, read);

            processed += read;
            index++;

            form.ReportProgress(processed, total, timer.Elapsed);

            CryptoCompat.ZeroMemory(nonce);
            CryptoCompat.ZeroMemory(tag);
        }

        byte[] sourceDigest = sourceHash.GetHashAndReset();
        CryptoCompat.WriteAll(output, sourceDigest);
        CryptoCompat.ZeroMemory(sourceDigest);
        output.Flush(true);

        CryptoCompat.ZeroMemory(plain);
        CryptoCompat.ZeroMemory(cipher);
        CryptoCompat.ZeroMemory(tag);
    }

    internal static void ValidateContainer(
        string path, byte[] key, byte[] expectedHeaderNonce, IProgressReporter form)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);

        byte[] magic = new byte[4];
        ReadExactly(fs, magic);

        if (magic[0] != 'P' || magic[1] != 'D' || magic[2] != 'S' || magic[3] != '1')
            throw new InvalidDataException(L.T("Container başlığı geçersiz.", "Invalid container header."));

        int version = fs.ReadByte();
        if (version != 1)
            throw new InvalidDataException(L.T("Container sürümü geçersiz.", "Invalid container version."));

        byte[] b4 = new byte[4];
        byte[] b8 = new byte[8];

        ReadExactly(fs, b4);
        int chunkSize = BitConverter.ToInt32(b4, 0);

        ReadExactly(fs, b8);
        long total = BitConverter.ToInt64(b8, 0);

        ReadExactly(fs, b8);
        long chunks = BitConverter.ToInt64(b8, 0);

        byte[] headerNonce = new byte[12];
        ReadExactly(fs, headerNonce);

        if (!CryptoCompat.FixedTimeEquals(headerNonce, expectedHeaderNonce))
            throw new CryptographicException(L.T("Nonce doğrulaması başarısız.", "Nonce validation failed."));

        if (chunkSize != ChunkSize || total < 0 || chunks < 0)
            throw new InvalidDataException(L.T("Container bilgileri geçersiz.", "Invalid container information."));

        using SecureRentedBuffer cipherLease = SecureRentedBuffer.Rent(ChunkSize);
        using SecureRentedBuffer plainLease = SecureRentedBuffer.Rent(ChunkSize);
        byte[] cipher = cipherLease.Buffer;
        byte[] plain = plainLease.Buffer;
        byte[] tag = new byte[16];

        using AesGcmCompat aes = new AesGcmCompat(key);
        using IncrementalHash plainHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long counted = 0;
        Stopwatch timer = Stopwatch.StartNew();

        for (long i = 0; i < chunks; i++)
        {
            form.ThrowIfCancellationRequested();

            ReadExactly(fs, b4);
            int length = BitConverter.ToInt32(b4, 0);

            if (length < 0 || length > ChunkSize)
                throw new InvalidDataException(L.T("Chunk uzunluğu geçersiz.", "Invalid chunk length."));

            byte[] nonce = new byte[12];
            ReadExactly(fs, nonce);
            ReadExactly(fs, tag);
            ReadExactly(fs, cipher, 0, length);

            aes.Decrypt(nonce,
                cipher, 0, length,
                tag,
                plain, 0);

            plainHash.AppendData(plain, 0, length);
            counted += length;
            form.ReportValidation(i + 1, chunks, timer.Elapsed);

            CryptoCompat.ZeroMemory(nonce);
            CryptoCompat.ZeroMemory(tag);
        }

        byte[] expectedDigest = new byte[32];
        ReadExactly(fs, expectedDigest);
        byte[] actualDigest = plainHash.GetHashAndReset();

        if (counted != total || fs.Position != fs.Length ||
            !CryptoCompat.FixedTimeEquals(expectedDigest, actualDigest))
            throw new InvalidDataException(L.T("Container doğrulaması başarısız.", "Container validation failed."));

        CryptoCompat.ZeroMemory(expectedDigest);
        CryptoCompat.ZeroMemory(actualDigest);
        CryptoCompat.ZeroMemory(cipher);
        CryptoCompat.ZeroMemory(plain);
        CryptoCompat.ZeroMemory(tag);
    }

    private static void WriteInt32(byte[] buffer, int value)
    {
        buffer[0] = (byte)value;
        buffer[1] = (byte)(value >> 8);
        buffer[2] = (byte)(value >> 16);
        buffer[3] = (byte)(value >> 24);
    }

    private static void WriteInt64(byte[] buffer, long value)
    {
        unchecked
        {
            ulong bits = (ulong)value;
            for (int i = 0; i < 8; i++)
                buffer[i] = (byte)(bits >> (i * 8));
        }
    }

    private static byte[] MakeNonce(byte[] headerNonce, long index)
    {
        byte[] nonce = new byte[12];
        MakeNonce(headerNonce, index, nonce);
        return nonce;
    }

    private static void MakeNonce(byte[] headerNonce, long index, byte[] nonce)
    {
        Buffer.BlockCopy(headerNonce, 0, nonce, 0, 12);

        byte[] idx = BitConverter.GetBytes(index);
        for (int i = 0; i < 8; i++)
            nonce[4 + i] ^= idx[i];
    }

    private static int ReadChunk(FileStream fs, byte[] buffer)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int n = fs.Read(buffer, offset, buffer.Length - offset);
            if (n == 0) break;
            offset += n;
        }

        return offset;
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        int readOffset = offset;
        int end = offset + count;

        while (readOffset < end)
        {
            int n = stream.Read(buffer, readOffset, end - readOffset);
            if (n == 0) throw new EndOfStreamException();
            readOffset += n;
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int n = stream.Read(buffer, offset, buffer.Length - offset);
            if (n == 0) throw new EndOfStreamException();
            offset += n;
        }
    }

}


internal sealed class ShellDestroyForm : Form, IProgressReporter
{
    private readonly string file;
    private bool started;
    private ProgressBar progress = null!;
    private Label status = null!;
    private Label detail = null!;
    private Button cancel = null!;
    private CancellationTokenSource? cts;

    public ShellDestroyForm(string file)
    {
        L.Load();
        this.file = file;

        Text = L.T("Kalıcı Olarak Yok Et", "Permanent Delete");
        Width = 520;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Font = new Font("Segoe UI", 9F);

        status = new Label { Left = 22, Top = 22, Width = 455, Height = 25 };
        progress = new ProgressBar { Left = 22, Top = 58, Width = 455, Height = 25, Minimum = 0, Maximum = 100 };
        detail = new Label { Left = 22, Top = 92, Width = 455, Height = 45 };
        cancel = new Button { Left = 355, Top = 145, Width = 122, Height = 32, Text = L.T("İptal", "Cancel") };

        Controls.AddRange(new Control[] { status, progress, detail, cancel });
        cancel.Click += (_, _) => cts?.Cancel();
    }

    protected override async void OnShown(EventArgs e)
{
    base.OnShown(e);

    if (started) return;
    started = true;

    TopMost = true;
    Activate();
    BringToFront();

    if (!File.Exists(file) && !Directory.Exists(file))
    {
        MessageBox.Show(
            this,
            L.T(
                "Dosya veya klasör bulunamadı:\n\n" + file,
                "File or folder not found:\n\n" + file),
            L.T("Kalıcı Olarak Yok Et", "Permanent Delete"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        Close();
        return;
    }

    bool isDirectory = Directory.Exists(file);

    string itemName = Path.GetFileName(
        file.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

    string itemTypeTr = isDirectory ? "klasörü" : "dosyayı";
    string itemTypeEn = isDirectory ? "folder" : "file";

    DialogResult answer = MessageBox.Show(
        this,
        L.T(
            $"Bu {itemTypeTr} kalıcı olarak silmek istediğinizden emin misiniz?\n\n" +
            itemName +
            "\n\nBu işlem geri alınamaz.",
            $"Are you sure you want to permanently delete this {itemTypeEn}?\n\n" +
            itemName +
            "\n\nThis operation cannot be undone."),
        L.T("Kalıcı Olarak Yok Et", "Permanent Delete"),
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2);

    if (answer != DialogResult.Yes)
    {
        Close();
        return;
    }

    Show();
    WindowState = FormWindowState.Normal;
    TopMost = true;
    Activate();
    BringToFront();

   cts = new CancellationTokenSource();

DateTime operationStartedAt = DateTime.Now;
Stopwatch operationTimer = Stopwatch.StartNew();

cancel.Enabled = true;
status.Text = L.T("Hazırlanıyor...", "Preparing...");
detail.Text = Path.GetFileName(file);

    int totalFiles = 0;
    long totalBytes = 0;
	var operationFiles = new List<string>();

    try
    {
        if (isDirectory)
{
    List<string> files = new();

    Stack<string> pending = new();
    pending.Push(file);

    while (pending.Count > 0)
    {
        cts.Token.ThrowIfCancellationRequested();

        string current = pending.Pop();
        string[] entries;

        try
        {
            entries = Directory.GetFileSystemEntries(current);
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                L.T(
                    "Klasör okunamadı:\n" + current,
                    "Directory could not be read:\n" + current),
                ex);
        }

        foreach (string entry in entries)
        {
            cts.Token.ThrowIfCancellationRequested();

            FileAttributes attributes;

            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    L.T(
                        "Dosya veya klasör özellikleri okunamadı:\n" + entry,
                        "File or directory attributes could not be read:\n" + entry),
                    ex);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (VoidEraseSettings.SkipReparsePoints)
                    continue;

                throw new InvalidOperationException(
                    L.T(
                        "Sembolik bağlantı veya reparse point içeren öğe bulundu:\n" + entry,
                        "A symbolic link or reparse-point item was found:\n" + entry));
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                pending.Push(entry);
            }
            else
{
    if ((attributes & FileAttributes.Hidden) != 0 &&
        !VoidEraseSettings.DeleteHiddenFiles)
    {
        continue;
    }

    files.Add(entry);
}
        }
    }

    totalFiles = files.Count;
    operationFiles.AddRange(files);

    foreach (string item in files)
    {
        cts.Token.ThrowIfCancellationRequested();

        try
        {
            totalBytes += new FileInfo(item).Length;
        }
        catch
        {
            // Dosya işlem başlamadan önce erişilemez hale geldiyse
            // ana işlem sırasında ayrıca hata değerlendirilecektir.
        }
    }

        }
        else
        {
            totalFiles = 1;
            totalBytes = new FileInfo(file).Length;
			operationFiles.Add(file);
        }

        List<string> skippedFiles = new List<string>();

int verifiedCount = await Task.Run(
    () =>
    {
        int count = Program.DestroyPath(
            file,
            this,
            out skippedFiles);

        return count;
    },
    cts.Token);
	if (skippedFiles.Count > 0)
{
    

    foreach (string skippedFile in skippedFiles)
    {
        HistoryStore.Append(
            skippedFile,
            0,
            "SKIPPED");
    }
}

        cts.Token.ThrowIfCancellationRequested();

        progress.Value = 100;

        status.Text = L.T(
            "Tamamlandı.",
            "Completed.");

        detail.Text = L.T(
            isDirectory
                ? "Klasör ve içeriği başarıyla kalıcı olarak silindi."
                : "Dosya başarıyla kalıcı olarak silindi.",
            isDirectory
                ? "Folder and its contents were permanently deleted successfully."
                : "File was permanently deleted successfully.");

        cancel.Enabled = false;

OperationResult operationResult = new OperationResult
{
    TargetPath = file,
    StartedAt = operationStartedAt,
    Elapsed = operationTimer.Elapsed,

  TotalFiles = totalFiles + skippedFiles.Count,
	TotalBytes = totalBytes,
	Successful = totalFiles,
	Failed = 0,
	Skipped = skippedFiles.Count,
	Verified = verifiedCount,
	VerificationCompleted = skippedFiles.Count == 0 && verifiedCount == totalFiles,
	KeyDestructionCompleted = skippedFiles.Count == 0 && verifiedCount == totalFiles,
	Cancelled = false
};

operationResult.SuccessfulFiles.AddRange(operationFiles);
operationResult.SkippedFiles.AddRange(skippedFiles);

MainForm.PersistNistSanitizationRecord(operationResult);

if (!isDirectory)
{
    HistoryStore.Append(file, totalBytes, "SUCCESS", true);
}
else
{
    HistoryStore.AppendBatch("SUCCESS", totalFiles, true);
}

using (OperationSummaryForm summary =
    new OperationSummaryForm(
        operationResult,
        L.English))
{
    summary.ShowDialog(this);
}

Close();
    }
    catch (OperationCanceledException)
    {
        status.Text = L.T(
            "İptal edildi.",
            "Cancelled.");

        detail.Text = L.T(
            "Orijinal dosya korunmuştur.",
            "The original file was preserved.");

        cancel.Enabled = false;
    }
    catch (Exception ex)
    {
        status.Text = L.T(
            "İşlem başarısız.",
            "Operation failed.");

        detail.Text = L.T(
            "Orijinal dosya korunmuş olabilir.",
            "The original file may have been preserved.");

        cancel.Enabled = false;

        MessageBox.Show(
            this,
            L.T(
                "İşlem başarısız oldu.\n\n" + ex.Message,
                "Operation failed.\n\n" + ex.Message),
            L.T("Kalıcı Olarak Yok Et", "Permanent Delete"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
    finally
    {
        cts?.Dispose();
        cts = null;
    }
}
    public void ReportProgress(long processed, long total, TimeSpan elapsed)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(() => ReportProgress(processed, total, elapsed));
            return;
        }

        int percent = total == 0 ? 100 :
            (int)CryptoCompat.Clamp(processed * 100L / total, 0, 100);

        progress.Value = percent;

        double seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        double mbps = processed / 1024d / 1024d / seconds;
        long remaining = Math.Max(0, total - processed);
        double remainingSeconds = mbps <= 0 ? 0 :
            remaining / 1024d / 1024d / mbps;

        status.Text = L.T("AES-256-GCM işleniyor... " + percent + "%", "Processing AES-256-GCM... " + percent + "%");
        detail.Text = L.T(
            $"{FormatSize(processed)} / {FormatSize(total)}   •   {mbps:0.0} MB/s   •   Kalan: {FormatTime(remainingSeconds)}",
            $"{FormatSize(processed)} / {FormatSize(total)}   •   {mbps:0.0} MB/s   •   Remaining: {FormatTime(remainingSeconds)}");

        TopMost = true;
    }

    public void ReportValidation(long current, long total, TimeSpan elapsed)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(() => ReportValidation(current, total, elapsed));
            return;
        }

        int percent = total == 0 ? 100 :
            (int)CryptoCompat.Clamp(current * 100L / total, 0, 100);

        progress.Value = percent;
        status.Text = L.T("Şifreli veri doğrulanıyor... " + percent + "%", "Verifying encrypted data... " + percent + "%");
        detail.Text = L.T($"{current:N0} / {total:N0} parça doğrulanıyor...", $"{current:N0} / {total:N0} chunks verifying...");
        TopMost = true;
    }

    public void ReportFinalizing()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ReportFinalizing);
            return;
        }

        progress.Value = 100;
        status.Text = L.T("Sonlandırılıyor...", "Finalizing...");
        detail.Text = L.T("Doğrulama tamamlandı.", "Verification completed.");
        TopMost = true;
    }

    public void ThrowIfCancellationRequested()
    {
        cts?.Token.ThrowIfCancellationRequested();
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:0.##} {units[i]}";
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--";
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} sa {t.Minutes:00} dk";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} dk {t.Seconds:00} sn";
        return L.T($"{t.Seconds} sn", $"{t.Seconds} sec");
    }
}

internal sealed class MainForm : Form, IProgressReporter
{
    private readonly Label fileLabel = new();
    private readonly Label sizeLabel = new();
    private readonly Label statusLabel = new();
    private readonly Label detailLabel = new();
    private readonly Label registryLabel = new();
    private readonly Label titleLabel = new();
    private readonly Label subtitleLabel = new();
    private readonly Label versionLabel = new();
    private readonly LinkLabel websiteLink = new();
    private readonly Button selectFileButton = new();
    private readonly Button selectFolderButton = new();
    private readonly Button destroyButton = new();
	private readonly Label hint = new();
    private readonly Button cancelButton = new();
    private readonly Button registryButton = new();
		private readonly Button historyButton = new();
		private readonly Button logsButton = new();
    private readonly Button languageButton = new();
    private readonly Button settingsButton = new();
    private readonly PictureBox logo = new();
    private readonly Panel fileCard = new();
    private readonly Panel processCard = new();
    private readonly Panel progressTrack = new();
    private readonly Panel progressFill = new();
    private readonly Panel footerLine = new();
    private readonly ToolTip registryToolTip = new();

    private readonly List<string> selectedItems = new();
    private CancellationTokenSource? cts;
    private bool running;
    private bool updateCheckRunning;
    private UsbTargetPreflightResult? currentUsbPreflight;
    private int lastProgressDispatchTick;

    private static readonly Color BackgroundColor = Color.FromArgb(244, 247, 250);
    private static readonly Color CardColor = Color.White;
    private static readonly Color CardBorder = Color.FromArgb(214, 222, 231);
    private static readonly Color TextPrimary = Color.FromArgb(31, 42, 52);
    private static readonly Color TextSecondary = Color.FromArgb(101, 115, 130);
    private static readonly Color Accent = Color.FromArgb(25, 150, 220);
    private static readonly Color AccentDark = Color.FromArgb(18, 112, 168);
    private static readonly Color Danger = Color.FromArgb(211, 63, 63);
    private static readonly Color DangerHover = Color.FromArgb(226, 78, 78);

    internal bool IsCancellationRequested => cts?.IsCancellationRequested == true;

    public MainForm(string? initialFile)
    {
        Text = "VoidErase";
        ClientSize = new Size(720, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        BackColor = BackgroundColor;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);

        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildHeader();
        BuildFileCard();
        BuildProcessCard();
        BuildActions();
        BuildFooter();

        if (!string.IsNullOrWhiteSpace(initialFile))
            SetSelection(new[] { initialFile });
        else
            SetIdle();

        UpdateRegistryStatus();

        Shown += async (_, _) =>
        {
            if (L.AutoUpdate)
                await CheckForUpdatesAsync(false);
        };

        FormClosing += (_, e) =>
        {
            if (!running) return;

            DialogResult answer = MessageBox.Show(
                this,
                L.T(
                    "Bir işlem devam ediyor. İptal etmek ve pencereyi kapatmak istiyor musunuz?\n\nTamamlanan dosyalar geri alınamaz; kalan dosyalar korunur.",
                    "An operation is still running. Cancel it and close the window?\n\nCompleted files cannot be restored; remaining files will be preserved."),
                L.T("İşlem devam ediyor", "Operation in progress"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer == DialogResult.Yes)
            {
                cts?.Cancel();
                e.Cancel = true;
            }
            else
            {
                e.Cancel = true;
            }
        };
    }

    private void BuildHeader()
    {
        logo.SetBounds(24, 20, 54, 54);
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        logo.BackColor = Color.Transparent;
        try
        {
            using Icon? appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            logo.Image = appIcon?.ToBitmap();
        }
        catch { }

        titleLabel.SetBounds(88, 18, 280, 34);
        titleLabel.Text = "VoidErase";
        titleLabel.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);
        titleLabel.ForeColor = TextPrimary;

        subtitleLabel.SetBounds(90, 51, 340, 22);
        subtitleLabel.Text = L.T("Dosyalarınızı kalıcı olarak silin.", "Permanently erase your files.");
        subtitleLabel.ForeColor = TextSecondary;
		subtitleLabel.Font = new Font("Segoe UI", 9.5F);

        settingsButton.SetBounds(607, 22, 34, 30);
settingsButton.Text = "⚙";
settingsButton.Font = new Font("Segoe UI Symbol", 12F);
StyleButton(settingsButton, CardColor, TextPrimary, false);
settingsButton.FlatAppearance.BorderColor = CardBorder;
settingsButton.Click += (_, _) => OpenSettings();
registryToolTip.SetToolTip(settingsButton, L.T("Ayarlar", "Settings"));

        languageButton.SetBounds(648, 22, 48, 30);
        languageButton.Text = L.T("EN", "TR");
        StyleButton(languageButton, Accent, Color.White, true);
        languageButton.Click += (_, _) =>
        {
            L.SetLanguage(!L.Turkish);
            Program.UpdateContextMenuLanguage();
            UpdateTexts();
        };
        registryToolTip.SetToolTip(languageButton, L.T("Dili değiştir", "Change language"));

        hint.SetBounds(500, 57, 196, 18);
        hint.Text = L.T("Doğrulanmış dosya silme • medya düzeyi garanti edilmez", "Verified file deletion • no media-level guarantee");
        hint.ForeColor = Color.FromArgb(30, 145, 88);
        hint.Font = new Font("Segoe UI", 8F);
        hint.TextAlign = ContentAlignment.MiddleRight;

        registryLabel.SetBounds(500, 76, 196, 18);
        registryLabel.TextAlign = ContentAlignment.MiddleRight;
        registryLabel.ForeColor = TextSecondary;
        registryLabel.AutoEllipsis = true;
        registryToolTip.SetToolTip(registryLabel, "");

        Controls.AddRange(new Control[] { logo, titleLabel, subtitleLabel, settingsButton, languageButton, hint, registryLabel });
    }

    private void BuildFileCard()
    {
        fileCard.SetBounds(24, 94, 672, 116);
        fileCard.BackColor = CardColor;
        fileCard.BorderStyle = BorderStyle.FixedSingle;

        Label heading = new()
        {
            Text = L.T("DOSYA / KLASÖR", "FILE / FOLDER"),
            ForeColor = Accent,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };
        heading.SetBounds(18, 14, 200, 20);

        fileLabel.SetBounds(18, 39, 500, 27);
        fileLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        fileLabel.ForeColor = TextPrimary;
        fileLabel.AutoEllipsis = true;

        sizeLabel.SetBounds(18, 70, 500, 20);
        sizeLabel.ForeColor = TextSecondary;

        selectFileButton.SetBounds(532, 30, 120, 34);
        selectFileButton.Text = L.T("Dosya Seç", "Select File");
        StyleButton(selectFileButton, Accent, Color.White, true);
        selectFileButton.Click += (_, _) => ChooseFiles();
        registryToolTip.SetToolTip(selectFileButton, L.T("Dosya seç", "Select a file"));

        selectFolderButton.SetBounds(532, 68, 120, 30);
        selectFolderButton.Text = L.T("Klasör Seç", "Select Folder");
        StyleButton(selectFolderButton, CardColor, TextPrimary, false);
        selectFolderButton.FlatAppearance.BorderColor = CardBorder;
        selectFolderButton.Click += (_, _) => ChooseFolder();
        registryToolTip.SetToolTip(selectFolderButton, L.T("Klasör seç", "Select a folder"));

        fileCard.Controls.AddRange(new Control[] { heading, fileLabel, sizeLabel, selectFileButton, selectFolderButton });
        Controls.Add(fileCard);
    }

    private void BuildProcessCard()
    {
        processCard.SetBounds(24, 222, 672, 126);
        processCard.BackColor = CardColor;
        processCard.BorderStyle = BorderStyle.FixedSingle;

        Label heading = new()
        {
            Text = L.T("İŞLEM", "PROCESS"),
            ForeColor = Accent,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };
        heading.SetBounds(18, 14, 180, 20);

        statusLabel.SetBounds(18, 39, 630, 24);
        statusLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        statusLabel.ForeColor = TextPrimary;

        detailLabel.SetBounds(18, 65, 630, 20);
        detailLabel.ForeColor = TextSecondary;
        detailLabel.AutoEllipsis = true;

        progressTrack.SetBounds(18, 93, 630, 10);
		progressTrack.BackColor = Color.FromArgb(231, 236, 241);
		progressFill.SetBounds(0, 0, 0, 10);
        progressFill.BackColor = Accent;
        progressTrack.Controls.Add(progressFill);

        processCard.Controls.AddRange(new Control[] { heading, statusLabel, detailLabel, progressTrack });
        Controls.Add(processCard);
    }

    private void BuildActions()
    {
        destroyButton.SetBounds(24, 362, 322, 42);
        destroyButton.Text = L.T("KALICI OLARAK SİL", "PERMANENT DELETE");
        destroyButton.Enabled = false;
        StyleButton(destroyButton, Color.FromArgb(224, 228, 233), Color.FromArgb(125, 132, 140), true);
        destroyButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        destroyButton.FlatAppearance.MouseOverBackColor = DangerHover;
        destroyButton.Click += async (_, _) => await StartDestroyAsync();
        registryToolTip.SetToolTip(destroyButton, L.T("Seçilen dosya veya klasörü güvenli biçimde işle", "Securely process the selected file or folder"));

        cancelButton.SetBounds(354, 362, 342, 42);
        cancelButton.Text = L.T("İptal", "Cancel");
        cancelButton.Enabled = false;
        StyleButton(cancelButton, CardColor, TextPrimary, false);
        cancelButton.FlatAppearance.BorderColor = CardBorder;
        cancelButton.Click += (_, _) => { if (running) cts?.Cancel(); };
        registryToolTip.SetToolTip(cancelButton, L.T("Devam eden işlemi iptal et", "Cancel the running operation"));

        registryButton.SetBounds(24, 414, 216, 34);
StyleButton(registryButton, CardColor, TextPrimary, false);
registryButton.FlatAppearance.BorderColor = CardBorder;
registryButton.Click += (_, _) => ToggleRegistry();
registryToolTip.SetToolTip(registryButton, L.T("Windows sağ tık menüsü entegrasyonunu yönet", "Manage the Windows context-menu integration"));

historyButton.SetBounds(252, 414, 216, 34);
historyButton.Text = L.T("İşlem Geçmişi", "History");
StyleButton(historyButton, CardColor, TextPrimary, false);
historyButton.FlatAppearance.BorderColor = CardBorder;
historyButton.Click += (_, _) => OpenHistory();
registryToolTip.SetToolTip(historyButton, L.T("İşlem geçmişini aç", "Open operation history"));

logsButton.SetBounds(480, 414, 216, 34);
logsButton.Text = L.T("Loglar", "Logs");
StyleButton(logsButton, CardColor, TextPrimary, false);
logsButton.FlatAppearance.BorderColor = CardBorder;
logsButton.Click += (_, _) => OpenLogs();
registryToolTip.SetToolTip(logsButton, L.T("NIST XML kayıt klasörünü aç", "Open the NIST XML records folder"));

        Controls.AddRange(new Control[]
{
    destroyButton,
    cancelButton,
    registryButton,
    historyButton,
    logsButton
});
    }

    private void BuildFooter()
    {
        footerLine.SetBounds(24, 456, 672, 1);
        footerLine.BackColor = CardBorder;
		
PictureBox authorLogo = new()
{
    SizeMode = PictureBoxSizeMode.Zoom,
    BackColor = Color.Transparent
};

authorLogo.SetBounds(315, 468, 90, 20);
authorLogo.SizeMode = PictureBoxSizeMode.Zoom;

try
{
    using (Stream? stream =
    typeof(MainForm).Assembly.GetManifestResourceStream("tuncay_gokturk.png"))
{
    if (stream != null)
    {
        using Image source = Image.FromStream(stream);
        authorLogo.Image = new Bitmap(source);
    }
}
}
catch
{
}

        versionLabel.SetBounds(24, 467, 140, 22);
        versionLabel.Text = Program.DisplayVersion;
        versionLabel.ForeColor = Accent;
        versionLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Underline);
        versionLabel.TextAlign = ContentAlignment.MiddleLeft;
        versionLabel.Cursor = Cursors.Hand;
        registryToolTip.SetToolTip(versionLabel, L.T("Güncellemeleri denetlemek için tıklayın", "Click to check for updates"));
        versionLabel.Click += async (_, _) => await CheckForUpdatesAsync(true);

        websiteLink.SetBounds(548, 467, 148, 22);
        websiteLink.Text = "tuncay.net.tr";
        websiteLink.TextAlign = ContentAlignment.MiddleRight;
        websiteLink.LinkColor = Accent;
        websiteLink.ActiveLinkColor = AccentDark;
        websiteLink.VisitedLinkColor = Accent;
        websiteLink.Cursor = Cursors.Hand;
        websiteLink.LinkBehavior = LinkBehavior.HoverUnderline;
        websiteLink.LinkClicked += (_, _) => OpenWebsite();
        registryToolTip.SetToolTip(websiteLink, L.T("VoidErase web sitesini aç", "Open the VoidErase website"));

Controls.AddRange(new Control[]
{
    footerLine,
    versionLabel,
    authorLogo,
    websiteLink
});
authorLogo.BringToFront();
    }

    private static void StyleButton(Button button, Color backColor, Color foreColor, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = accent ? backColor : CardBorder;
        button.FlatAppearance.MouseDownBackColor = accent ? AccentDark : Color.FromArgb(238, 242, 246);
        button.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(48, 166, 226) : Color.FromArgb(242, 245, 248);
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
    }

    private void UpdateTexts()
    {
        subtitleLabel.Text = L.T("Dosyalarınızı kalıcı olarak silin.", "Permanently erase your files.");
        languageButton.Text = L.T("EN", "TR");
        destroyButton.Text = L.T("KALICI OLARAK SİL", "PERMANENT DELETE");
        cancelButton.Text = L.T("İptal", "Cancel");
        hint.Text = L.T(
    "Güvenli silme • AES-256-GCM",
    "Secure erasure • AES-256-GCM");
        UpdateToolTips();
        registryButton.Text = RegistryIsInstalled()
            ? L.T("Sağ Tık Menüsünü KALDIR", "REMOVE CONTEXT MENU")
            : L.T("Sağ Tık Menüsünü ETKİNLEŞTİR", "ENABLE CONTEXT MENU");
        ((Label)fileCard.Controls[0]).Text = L.T("DOSYA / KLASÖR", "FILE / FOLDER");
        selectFileButton.Text = L.T("Dosya Seç", "Select File");
        selectFolderButton.Text = L.T("Klasör Seç", "Select Folder");
        ((Label)processCard.Controls[0]).Text = L.T("İŞLEM", "PROCESS");

        if (selectedItems.Count == 0) SetIdle();
        else RefreshSelectionSummary();
        UpdateRegistryStatus();
    }

    private void UpdateToolTips()
    {
        registryToolTip.SetToolTip(settingsButton, L.T("Ayarlar", "Settings"));
        registryToolTip.SetToolTip(languageButton, L.T("Dili değiştir", "Change language"));
        registryToolTip.SetToolTip(selectFileButton, L.T("Dosya seç", "Select a file"));
        registryToolTip.SetToolTip(selectFolderButton, L.T("Klasör seç", "Select a folder"));
        registryToolTip.SetToolTip(destroyButton, L.T("Seçilen dosya veya klasörü güvenli biçimde işle", "Securely process the selected file or folder"));
        registryToolTip.SetToolTip(cancelButton, L.T("Devam eden işlemi iptal et", "Cancel the running operation"));
        registryToolTip.SetToolTip(registryButton, L.T("Windows sağ tık menüsü entegrasyonunu yönet", "Manage the Windows context-menu integration"));
        registryToolTip.SetToolTip(historyButton, L.T("İşlem geçmişini aç", "Open operation history"));
        registryToolTip.SetToolTip(logsButton, L.T("NIST XML kayıt klasörünü aç", "Open the NIST XML records folder"));
        registryToolTip.SetToolTip(versionLabel, L.T("Güncellemeleri denetlemek için tıklayın", "Click to check for updates"));
        registryToolTip.SetToolTip(websiteLink, L.T("VoidErase web sitesini aç", "Open the VoidErase website"));
    }

    private void SetIdle()
    {
        fileLabel.Text = L.T("Dosya veya klasör seçilmedi.", "No file or folder selected.");
        sizeLabel.Text = "";
        statusLabel.Text = L.T("Hazır", "Ready");
        detailLabel.Text = "";
        SetProgress(0);
        destroyButton.Enabled = false;
        cancelButton.Enabled = false;
        selectFileButton.Enabled = true;
        selectFolderButton.Enabled = true;
        destroyButton.ForeColor = Color.FromArgb(145, 150, 157);
    }

    private void SetSelection(IEnumerable<string> paths)
    {
        currentUsbPreflight = null;
        selectedItems.Clear();
        foreach (string path in paths.Where(File.Exists))
            selectedItems.Add(path);
        RefreshSelectionSummary();
    }

    private void SetFolderSelection(string folder)
    {
        currentUsbPreflight = null;
        selectedItems.Clear();

        if (!Directory.Exists(folder))
        {
            RefreshSelectionSummary();
            return;
        }

        selectedItems.Add(folder);
        RefreshSelectionSummary();

        // Root-level USB selection gets an automatic read-only target preflight.
        // No destructive operation is triggered here.
        string root = Path.GetPathRoot(Path.GetFullPath(folder)) ?? "";
        string normalizedFolder = Path.GetFullPath(folder).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        string normalizedRoot = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (!string.IsNullOrWhiteSpace(root) &&
            string.Equals(normalizedFolder, normalizedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            UsbTargetPreflightResult probe =
                UsbTargetPreflight.AnalyzeDrive(root);

            if (probe.DiskNumber >= 0 &&
                string.Equals(probe.BusType, "USB",
                    StringComparison.OrdinalIgnoreCase))
            {
                    ShowUsbTargetPreflight(probe);
            }
        }
    }

    private void ShowUsbTargetPreflight(UsbTargetPreflightResult probe)
    {
        string state = probe.State.ToString().ToUpperInvariant();

        string message =
            "=== VoidErase USB Target Preflight ===\r\n\r\n" +
            "Target: " + probe.DriveRoot + "\r\n" +
            "Physical disk: " + (probe.PhysicalDrive ?? "(unknown)") + "\r\n" +
            "Disk number: " + probe.DiskNumber + "\r\n" +
            "Model: " + (string.IsNullOrWhiteSpace(probe.Model) ? "(unknown)" : probe.Model) + "\r\n" +
            "Serial: " + (string.IsNullOrWhiteSpace(probe.SerialNumber) ? "(unknown)" : probe.SerialNumber) + "\r\n" +
            "Bus: " + (string.IsNullOrWhiteSpace(probe.BusType) ? "(unknown)" : probe.BusType) + "\r\n" +
            "Media: " + (string.IsNullOrWhiteSpace(probe.MediaType) ? "(unknown)" : probe.MediaType) + "\r\n" +
            "Disk size: " + probe.DiskSizeBytes.ToString("N0") + " bytes\r\n" +
            "System disk: " + (probe.IsSystem ? "Yes" : "No") + "\r\n" +
            "Boot disk: " + (probe.IsBoot ? "Yes" : "No") + "\r\n" +
            "Offline: " + (probe.IsOffline ? "Yes" : "No") + "\r\n" +
            "Read-only: " + (probe.IsReadOnly ? "Yes" : "No") + "\r\n\r\n" +
            "=== Safety Gate ===\r\n\r\n" +
            "Result: " + state + "\r\n\r\n" +
            "Scope:\r\n" +
            probe.Scope +
            "\r\n\r\n" +
            "Reason:\r\n" +
            probe.Reason +
            "\r\n\r\n" +
            "DRY RUN ONLY — no erase, overwrite, sanitize, format, " +
            "TRIM, IOCTL or device command was executed.";

        MessageBox.Show(
            this,
            message,
            "VoidErase USB Target Preflight",
            MessageBoxButtons.OK,
            probe.State == UsbTargetPreflightState.Pass
                ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning);

        // A failed USB target preflight must not leave the destructive button enabled.
        if (probe.State != UsbTargetPreflightState.Pass)
        {
            destroyButton.Enabled = false;
            destroyButton.ForeColor = Color.FromArgb(145, 150, 157);
        }
    }

    private void RefreshSelectionSummary()
    {
        if (selectedItems.Count == 0) { SetIdle(); return; }

        long total = 0;
        long count = 0;
        foreach (string item in selectedItems)
        {
            if (File.Exists(item)) { total += new FileInfo(item).Length; count++; }
            else if (Directory.Exists(item))
            {
                try
{
    foreach (string file in ExpandFilesForSummary(item))
    {
        try
        {
            total += new FileInfo(file).Length;
            count++;
        }
        catch
        {
        }
    }
}
catch
{
    // Özet ekranında erişilemeyen/reparse öğeler nedeniyle
    // seçim tamamen başarısız sayılmaz.
}
            }
        }

        string name = selectedItems.Count == 1 ? Path.GetFileName(selectedItems[0].TrimEnd(Path.DirectorySeparatorChar)) : L.T($"{selectedItems.Count} öğe seçildi", $"{selectedItems.Count} items selected");
        fileLabel.Text = name;
        sizeLabel.Text = L.T($"Toplam: {count:N0} dosya • {FormatSize(total)}", $"Total: {count:N0} files • {FormatSize(total)}");
        statusLabel.Text = L.T("Yok etmeye hazır.", "Ready to erase.");
        detailLabel.Text = selectedItems.Count == 1 ? selectedItems[0] : L.T("Birden fazla öğe seçildi.", "Multiple items selected.");
        detailLabel.ForeColor = TextSecondary;
        SetProgress(0);
        bool selectionEnabled = selectedItems.Count > 0;

        if (currentUsbPreflight != null &&
            currentUsbPreflight.State != UsbTargetPreflightState.Pass)
        {
            selectionEnabled = false;
        }

        string blockedReason;
        if (selectionEnabled && IsMandatoryBlockedSelection(out blockedReason))
        {
            selectionEnabled = false;
            statusLabel.Text = L.T("Güvenlik nedeniyle engellendi.", "Blocked for safety.");
            detailLabel.Text = blockedReason;
            detailLabel.ForeColor = Danger;
        }

        destroyButton.Enabled = selectionEnabled;
        destroyButton.BackColor = selectionEnabled ? Danger : Color.FromArgb(224, 228, 233);
        destroyButton.ForeColor = selectionEnabled ? Color.White : Color.FromArgb(125, 132, 140);
        destroyButton.FlatAppearance.BorderColor = selectionEnabled ? Danger : Color.FromArgb(190, 198, 207);
        destroyButton.FlatAppearance.MouseOverBackColor = selectionEnabled ? DangerHover : Color.FromArgb(224, 228, 233);
    }

    private bool IsMandatoryBlockedSelection(out string reason)
    {
        foreach (string item in selectedItems)
        {
            string full;
            try
            {
                full = Path.GetFullPath(item).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                reason = L.T("Hedef yolu doğrulanamadı.", "The target path could not be verified.");
                return true;
            }

            if (VoidEraseSafety.IsMandatoryProtectedPath(full))
            {
                reason = L.T(
                    "Windows veya Program Files sistem yolu korunur; bu hedef seçilemez.",
                    "Windows or Program Files system paths are protected and cannot be selected.");
                return true;
            }

            if (VoidEraseSafety.IsSameAsExecutable(full))
            {
                reason = L.T(
                    "VoidErase uygulamasının kendi EXE dosyası korunur.",
                    "The VoidErase executable is protected.");
                return true;
            }

            string root = Path.GetPathRoot(full) ?? "";
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
            if (Directory.Exists(full) && !string.IsNullOrWhiteSpace(root) &&
                string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                reason = L.T(
                    "Sistem sürücüsünün kökü korunur; bu hedef seçilemez.",
                    "The root of the system drive is protected and cannot be selected.");
                return true;
            }
        }

        reason = "";
        return false;
    }

    private void ChooseFiles()
    {
        if (running) return;
        using OpenFileDialog dlg = new()
        {
            Title = L.T("Kalıcı olarak yok edilecek dosyaları seçin", "Select files to permanently erase"),
            CheckFileExists = true,
            Multiselect = true,
            RestoreDirectory = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetSelection(dlg.FileNames);
    }

    private void ChooseFolder()
    {
        if (running) return;
        using FolderBrowserDialog dlg = new()
        {
            Description = L.T("Kalıcı olarak yok edilecek klasörü seçin", "Select a folder to permanently erase")
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetFolderSelection(dlg.SelectedPath);
    }

private IEnumerable<string> ExpandFilesForSummary(string directory)
{
    Stack<string> pending = new Stack<string>();
    pending.Push(directory);

    while (pending.Count > 0)
    {
        string current = pending.Pop();

        string[] entries;

        try
        {
            entries = Directory.GetFileSystemEntries(current);
        }
        catch
        {
            continue;
        }

        foreach (string entry in entries)
        {
            FileAttributes attributes;

            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            if ((attributes & FileAttributes.Directory) != 0)
                pending.Push(entry);
            else
                yield return entry;
        }
    }
}

  private List<string> ExpandSelectedFiles(out List<string> skippedFiles)
{
    var files = new List<string>();
    skippedFiles = new List<string>();

    foreach (string item in selectedItems)
    {
        if (File.Exists(item))
        {
            files.Add(item);
            continue;
        }

        if (!Directory.Exists(item))
            continue;

        Stack<string> pending = new Stack<string>();
        pending.Push(item);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            string[] entries;

            try
            {
                entries = Directory.GetFileSystemEntries(current);
            }
            catch
            {
                continue;
            }

            foreach (string entry in entries)
            {
                FileAttributes attributes;

                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (VoidEraseSettings.SkipReparsePoints)
                        continue;

                    throw new InvalidOperationException(
                        L.T(
                            "Sembolik bağlantı veya reparse point içeren öğe seçilen klasörde bulundu:\n" + entry,
                            "A symbolic link or reparse-point item was found in the selected folder:\n" + entry));
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
{
    if ((attributes & FileAttributes.Hidden) != 0 &&
        !VoidEraseSettings.DeleteHiddenFiles)
    {
        continue;
    }

    files.Add(entry);
}
            }
        }
    }

    return files
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
private void formSafeReportProgress(
    long completedBytes,
    long totalBytes,
    TimeSpan elapsed)
{
    if (IsDisposed || Disposing)
        return;

    if (InvokeRequired)
    {
        int now = Environment.TickCount;
        if (completedBytes < totalBytes && unchecked(now - lastProgressDispatchTick) < 100)
            return;
        lastProgressDispatchTick = now;

        BeginInvoke(new Action(() =>
            ReportProgress(
                completedBytes,
                Math.Max(totalBytes, 1),
                elapsed)));

        return;
    }

    ReportProgress(
        completedBytes,
        Math.Max(totalBytes, 1),
        elapsed);
}

    private async Task StartDestroyAsync()
{
    if (running || selectedItems.Count == 0)
        return;

    string blockedReason;
    if (IsMandatoryBlockedSelection(out blockedReason))
    {
        MessageBox.Show(
            this,
            blockedReason,
            L.T("İşlem engellendi", "Operation blocked"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        RefreshSelectionSummary();
        return;
    }

    // Final read-only USB target recheck immediately before confirmation.
    // This does not erase or modify the device.
    if (selectedItems.Count == 1 && Directory.Exists(selectedItems[0]))
    {
        string selectedFullPath = Path.GetFullPath(selectedItems[0]);
        string selectedRoot = Path.GetPathRoot(selectedFullPath) ?? "";
        string normalizedSelected = selectedFullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string normalizedSelectedRoot = selectedRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (!string.IsNullOrWhiteSpace(selectedRoot) &&
            string.Equals(normalizedSelected, normalizedSelectedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            UsbTargetPreflightResult finalUsbProbe =
                UsbTargetPreflight.AnalyzeDrive(selectedRoot);

            if (finalUsbProbe.DiskNumber >= 0 &&
                string.Equals(finalUsbProbe.BusType, "USB",
                    StringComparison.OrdinalIgnoreCase))
            {
                currentUsbPreflight = finalUsbProbe;

                if (finalUsbProbe.State != UsbTargetPreflightState.Pass)
                {
                    ShowUsbTargetPreflight(finalUsbProbe);
                    return;
                }
            }
        }
    }

    if (L.ConfirmBeforeErase)
    {
        DialogResult answer = MessageBox.Show(
            this,
            L.T(
                "Bu işlem geri alınamaz.\n\n" +
                "Seçilen dosyalar AES-256-GCM ile işlenecek ve " +
                "doğrulama tamamlandıktan sonra orijinalleri silinecektir.\n\n" +
                "Devam etmek istiyor musunuz?",
                "This operation cannot be undone.\n\n" +
                "Selected files will be processed with AES-256-GCM " +
                "and originals will be deleted only after verification.\n\n" +
                "Continue?"),
            L.T("Kalıcı Olarak Yok Et", "Permanent Delete"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
            return;
    }

    running = true;
    lastProgressDispatchTick = 0;
    destroyButton.Enabled = false;
    selectFileButton.Enabled = false;
    selectFolderButton.Enabled = false;
    registryButton.Enabled = false;
    historyButton.Enabled = false;
    logsButton.Enabled = false;
    cancelButton.Enabled = true;
    statusLabel.Text = L.T("Hazırlanıyor...", "Preparing...");
    detailLabel.Text = L.T("Güvenlik kontrolleri ve dosya listesi hazırlanıyor.", "Preparing safety checks and file list.");
    VoidEraseLogger.Write($"Operation started; selectedItems={selectedItems.Count}");

    cts = new CancellationTokenSource();

    DateTime operationStartedAt = DateTime.Now;
    Stopwatch operationTimer = Stopwatch.StartNew();

    List<string> files;
List<string> skippedFiles;

try
{
    files = ExpandSelectedFiles(out skippedFiles)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
    catch (Exception ex)
    {
        MessageBox.Show(
            this,
            L.T(
                "Dosya listesi oluşturulamadı:\n\n" + ex.Message,
                "The file list could not be created:\n\n" + ex.Message),
            "VoidErase",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        running = false;
        destroyButton.Enabled = true;
        return;
    }

    if (files.Count == 0)
{
    bool hasDirectorySelection = selectedItems.Any(Directory.Exists);

    if (!hasDirectorySelection)
    {
        MessageBox.Show(
            this,
            L.T(
                "İşlenecek dosya bulunamadı.",
                "No files were found to process."),
            "VoidErase",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        running = false;
        destroyButton.Enabled = true;
        return;
    }
}


var result = new OperationResult
{
    TargetPath = selectedItems.Count == 1
        ? selectedItems[0]
        : L.T("Birden fazla öğe", "Multiple items"),

    StartedAt = operationStartedAt,
    TotalFiles = files.Count,

    // Gerçek uygulanan yöntemi raporla.
    // Cihaz seviyesinde NIST Purge iddiasında bulunma.
    SanitizationMethod = "Verified cryptographic transformation + deletion",
    SanitizationStandard = "NIST SP 800-88 Rev. 2 aligned reporting",
    VerificationMethod = "AES-256-GCM authentication + SHA-256",
	KeyDestructionCompleted = false,    

    ErasureMethod = "Cryptographic transformation + verified deletion",
    EncryptionAlgorithm = "AES-256-GCM",
    VerificationAlgorithm = "SHA-256",
    
};

    result.PreOperationIdentity = MediaIdentityValidation.Capture(result.TargetPath);

    long totalBytes = 0;

    foreach (string file in files)
    {
        try
        {
            totalBytes += new FileInfo(file).Length;
        }
        catch
        {
            // Boyut okunamazsa silme işlemini burada durdurmuyoruz.
        }
    }

    result.TotalBytes = totalBytes;

    long completedBytes = 0;

    try
    {
        for (int i = 0; i < files.Count; i++)
        {
            cts.Token.ThrowIfCancellationRequested();

            string file = files[i];

            long fileSize = 0;

            try
            {
                fileSize = new FileInfo(file).Length;
            }
            catch
            {
                fileSize = 0;
            }

            statusLabel.Text = L.T(
                $"İşleniyor... {i + 1}/{files.Count}",
                $"Processing... {i + 1}/{files.Count}");

            detailLabel.Text = Path.GetFileName(file);
            detailLabel.ForeColor = TextSecondary;

            SetProgress(0);

            try
            {
           await Task.Run(
    () => Program.DestroyFile(file, this),
    cts.Token);

                result.Successful++;
				result.Verified++;
				result.VerificationCompleted = true;
				
				result.SuccessfulFiles.Add(file);

                HistoryStore.Append(
                    file,
                    fileSize,
                    "SUCCESS",
					true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
catch (UnauthorizedAccessException)
{
    result.Skipped++;

    result.SkippedFiles.Add(
        file + " — " +
        L.T(
            "Erişim izni yok.",
            "Access denied."));

    HistoryStore.Append(
        file,
        fileSize,
        "SKIPPED");
}
            catch (InvalidOperationException ex)
            {
                // Reparse point / sistem dosyası gibi güvenlik nedeniyle
                // işlenmeyen öğeler "Skipped" olarak raporlanır.
                result.Skipped++;
                result.SkippedFiles.Add(
                    file + " — " + ex.Message);

                HistoryStore.Append(
                    file,
                    fileSize,
                    "SKIPPED");
            }
         catch (IOException ex)
{
    result.Failed++;

    result.FailedFiles.Add(
        file + " — " +
        L.T(
            "G/Ç hatası: " + ex.Message,
            "I/O error: " + ex.Message));

    HistoryStore.Append(
        file,
        fileSize,
        "FAILED");
}

            completedBytes += fileSize;

            formSafeReportProgress(
                completedBytes,
                Math.Max(totalBytes, 1),
                operationTimer.Elapsed);
        }

        // Sadece tamamen boşalan seçili klasörleri kaldır.
        foreach (string folder in selectedItems.Where(Directory.Exists))
        {
            cts.Token.ThrowIfCancellationRequested();

            try
            {
                DirectoryInfo info = new DirectoryInfo(folder);

                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder, false);
            }
            catch
            {
                // Klasör silinemiyorsa dosya sonuçlarını bozma.
            }
        }

  operationTimer.Stop();

result.Elapsed = operationTimer.Elapsed;
result.Cancelled = false;

SetProgress(100);

if (result.Failed == 0 && result.Skipped == 0)
{
    progressFill.BackColor = Color.FromArgb(30, 145, 88);

    statusLabel.Text = L.T(
        "✓ Silme tamamlandı",
        "✓ Erasure completed");

    detailLabel.Text = L.T(
        $"{result.Successful:N0} dosya başarıyla yok edildi • " +
        $"{FormatSize(result.TotalBytes)} • " +
        $"{FormatTime(result.Elapsed.TotalSeconds)}",
        $"{result.Successful:N0} files successfully erased • " +
        $"{FormatSize(result.TotalBytes)} • " +
        $"{FormatTime(result.Elapsed.TotalSeconds)}");

    detailLabel.ForeColor = Color.FromArgb(30, 145, 88);
}
else
{
    progressFill.BackColor = Color.FromArgb(190, 130, 35);

    statusLabel.Text = L.T(
        "İşlem tamamlandı",
        "Operation completed");

    detailLabel.Text = L.T(
        $"{result.Successful:N0} başarılı • " +
        $"{result.Failed:N0} başarısız • " +
        $"{result.Skipped:N0} atlandı • " +
        $"{FormatTime(result.Elapsed.TotalSeconds)}",
        $"{result.Successful:N0} successful • " +
        $"{result.Failed:N0} failed • " +
        $"{result.Skipped:N0} skipped • " +
        $"{FormatTime(result.Elapsed.TotalSeconds)}");

    detailLabel.ForeColor = Color.FromArgb(190, 130, 35);
}

selectedItems.Clear();

PersistNistSanitizationRecord(result);
ShowOperationSummary(result);
    }
    catch (OperationCanceledException)
    {
        VoidEraseLogger.Write("Operation cancelled by user or cancellation request.");
        operationTimer.Stop();

        result.Elapsed = operationTimer.Elapsed;
        result.Cancelled = true;

        statusLabel.Text = L.T(
            "İptal edildi.",
            "Cancelled.");

        detailLabel.Text = L.T(
            "Tamamlanan dosyalar işlendi; kalan dosyalar korunmuştur.",
            "Completed files were processed; remaining files were preserved.");

        detailLabel.ForeColor = Color.FromArgb(190, 70, 70);

        SetProgress(
            totalBytes > 0
                ? (int)Math.Min(
                    100,
                    completedBytes * 100L / totalBytes)
                : 0);

        PersistNistSanitizationRecord(result);
        ShowOperationSummary(result);
    }
    catch (Exception ex)
    {
        VoidEraseLogger.Error("Operation failed in main workflow.", ex);
        statusLabel.Text = L.T("İşlem başarısız.", "Operation failed.");
        detailLabel.Text = L.T("Orijinal dosya korunmuş olabilir.", "The original file may have been preserved.");
        detailLabel.ForeColor = Danger;
        MessageBox.Show(
            this,
            L.T("İşlem sırasında beklenmeyen bir hata oluştu. Ayrıntılar günlük dosyasına kaydedildi.", "An unexpected error occurred during the operation. Details were written to the log."),
            "VoidErase",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
    finally
    {
        try { HistoryStore.FlushPending(); } catch { }
        cts.Dispose();
        cts = null;

        running = false;
        destroyButton.Enabled = selectedItems.Count > 0;
        selectFileButton.Enabled = true;
        selectFolderButton.Enabled = true;
        registryButton.Enabled = true;
        historyButton.Enabled = true;
        logsButton.Enabled = true;
        cancelButton.Enabled = false;
    }
}

    internal static void PersistNistSanitizationRecord(OperationResult result)
    {
        try
        {
            result.PostOperationIdentity = MediaIdentityValidation.Capture(result.TargetPath);
            IdentityComparisonResult identity = MediaIdentityValidation.Compare(
                result.PreOperationIdentity,
                result.PostOperationIdentity,
                L.English);
            result.IdentityMatch = identity.Match;
            result.IdentityValidation = identity.Status + " — " + identity.Details;

            NistSanitizationRecord record =
                NistSanitizationRecordFactory.FromOperationResult(result, L.English);
            string path = NistSanitizationRecordStore.Save(record);
            result.NistRecordPath = path;
            result.NistCompatibility = record.Compatibility;
            result.NistValidationRequired = record.ValidationRequired;
            result.NistDecisionReason = record.DecisionReason;
            result.NistMediaSummary =
                (record.Media.MediaType ?? "") + " / " +
                (record.Media.Model ?? "") + " / " +
                (record.Media.PhysicalDrive ?? "");
            VoidEraseLogger.Write("NIST sanitization record saved: " + path);
        }
        catch (Exception ex)
        {
            // A reporting failure must never convert a completed erase result
            // into a false failure. Keep the failure visible in the application log.
            VoidEraseLogger.Error("NIST sanitization record could not be saved.", ex);
        }
    }

    private void SetControlsRunning(bool active)
    {
        selectFileButton.Enabled = !active;
        selectFolderButton.Enabled = !active;
        destroyButton.Enabled = false;
        registryButton.Enabled = !active;
        settingsButton.Enabled = !active;
        languageButton.Enabled = !active;
        cancelButton.Enabled = active;
    }

    private void ToggleRegistry()
    {
        if (running) return;
        try
        {
            if (RegistryIsInstalled())
            {
                if (MessageBox.Show(this, L.T("Sağ tık menüsü kaldırılacak.\n\nDevam?", "The context-menu entry will be removed.\n\nContinue?"), L.T("Sağ Tık Menüsü", "Context Menu"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Program.UninstallContextMenu();
            }
            else
            {
                if (!Program.InstallContextMenu(false)) throw new InvalidOperationException(L.T("Registry kaydı doğrulanamadı.", "The Registry entry could not be verified."));
                MessageBox.Show(this, L.T("Sağ tık menüsü etkinleştirildi.", "Context menu enabled."), L.T("Sağ Tık Menüsü", "Context Menu"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            UpdateRegistryStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("Registry işlemi başarısız:\n\n" + ex.Message, "Registry operation failed:\n\n" + ex.Message), "VoidErase", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool RegistryIsInstalled() => IsContextMenuKeyInstalled(Program.MenuKey) || IsContextMenuKeyInstalled(Program.DirectoryMenuKey) || IsContextMenuKeyInstalled(Program.LegacyMenuKey) || IsContextMenuKeyInstalled(Program.LegacyDirectoryMenuKey);

    private static bool IsContextMenuKeyInstalled(string menuKey)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(menuKey);
        if (key == null) return false;
        using RegistryKey? commandKey = key.OpenSubKey("command");
        string? command = commandKey?.GetValue("") as string;
        return !string.IsNullOrWhiteSpace(command) && command.IndexOf("--destroy", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void UpdateRegistryStatus()
    {
        bool installed = RegistryIsInstalled();
		historyButton.Text = L.T("İşlem Geçmişi", "History");
		logsButton.Text = L.T("Loglar", "Logs");
        registryButton.Text = installed ? L.T("Sağ Tık Menüsünü KALDIR", "REMOVE CONTEXT MENU") : L.T("Sağ Tık Menüsünü ETKİNLEŞTİR", "ENABLE CONTEXT MENU");
        registryLabel.Text = installed ? L.T("✓ Sağ tık menüsü etkin.", "✓ Context menu enabled.") : L.T("✕ Sağ tık menüsü etkin değil.", "✕ Context menu is not enabled.");
        registryLabel.ForeColor = installed ? Color.FromArgb(30, 145, 88) : Color.FromArgb(198, 70, 70);
        registryToolTip.SetToolTip(registryLabel, installed ? Program.GetExePath() : L.T("Kurulu değil.", "Not installed."));
    }

    private void OpenSettings()
    {
        using SettingsForm settings = new();
        if (settings.ShowDialog(this) == DialogResult.OK)
        {
            UpdateTexts();
            if (L.AutoUpdate && !updateCheckRunning) _ = CheckForUpdatesAsync(false);
        }
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(VoidEraseLogger.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + VoidEraseLogger.LogDirectory + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                L.T("Log klasörü açılamadı.\\n\\n" + ex.Message, "The log folder could not be opened.\\n\\n" + ex.Message),
                L.T("Loglar", "Logs"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OpenHistory()
    {
    if (running)
        return;

    using HistoryForm history = new();
    history.ShowDialog(this);
}
    private void OpenWebsite()
    {
        try { Process.Start(new ProcessStartInfo { FileName = "https://tuncay.net.tr", UseShellExecute = true }); }
        catch { }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (updateCheckRunning || running) return;
        updateCheckRunning = true;
        string original = versionLabel.Text;
        if (interactive) { versionLabel.Enabled = false; versionLabel.Text = L.T("Kontrol...", "Checking..."); }
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VoidErase/" + Program.AppVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/tuncaycandan/VoidErase/releases/latest");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (interactive) MessageBox.Show(this, L.T("GitHub'da henüz yayınlanmış bir sürüm bulunamadı.", "No published GitHub release was found yet."), L.T("Güncelleme", "Update"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            string tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            string latestText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(latestText, out Version? latest) || !Version.TryParse(Program.AppVersion, out Version? current))
                throw new InvalidOperationException(L.T("Sürüm bilgisi geçersiz.", "Invalid version information."));
            if (latest <= current)
            {
                if (interactive) MessageBox.Show(this, L.T($"VoidErase güncel.\n\nMevcut sürüm: {Program.DisplayVersion}", $"VoidErase is up to date.\n\nCurrent version: {Program.DisplayVersion}"), L.T("Güncelleme", "Update"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string? url = null; string? digest = null;
            Match assetMatch = Regex.Match(json,
                "\"name\"\\s*:\\s*\"VoidErase\\.exe\".*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"(?:.*?\"digest\"\\s*:\\s*\"([^\"]+)\")?",
                RegexOptions.Singleline);
            if (assetMatch.Success)
            {
                url = assetMatch.Groups[1].Value;
                digest = assetMatch.Groups[2].Success ? assetMatch.Groups[2].Value : null;
            }
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException(L.T("Release içinde VoidErase.exe bulunamadı.", "VoidErase.exe was not found in the release."));
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException(L.T("Güncelleme SHA-256 özeti olmadan güvenli biçimde yüklenemez.", "The update cannot be installed safely without a SHA-256 digest."));
            if (!interactive) interactive = MessageBox.Show(this, L.T($"Yeni sürüm v{latest} bulundu. Güncellensin mi?", $"New version v{latest} is available. Update now?"), L.T("Güncelleme", "Update"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
            else interactive = MessageBox.Show(this, L.T($"Yeni sürüm v{latest} bulundu.\n\nŞimdi indirip yüklemek ister misiniz?", $"New version v{latest} is available.\n\nDownload and install it now?"), L.T("Güncelleme", "Update"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
            if (!interactive) return;

            if (interactive) versionLabel.Text = L.T("İndiriliyor...", "Downloading...");
            string target = Program.GetExePath();
            string temp = target + ".update";
            if (File.Exists(temp)) File.Delete(temp);
            using HttpResponseMessage download = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            download.EnsureSuccessStatusCode();
            using Stream source = await download.Content.ReadAsStreamAsync();
            using FileStream dest = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(dest); await dest.FlushAsync();
            if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream verify = new(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
                string actual;
                using (SHA256 sha = SHA256.Create())
                    actual = CryptoCompat.ToHexString(sha.ComputeHash(verify));
                if (!string.Equals(actual, digest.Substring(7).Trim(), StringComparison.OrdinalIgnoreCase)) throw new CryptographicException(L.T("İndirilen dosyanın SHA-256 doğrulaması başarısız oldu.", "Downloaded file SHA-256 verification failed."));
            }
            string script = Path.Combine(Path.GetTempPath(), "VoidEraseUpdate_" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(script, $"@echo off\r\nset TARGET={QuoteForCmd(target)}\r\nset NEWFILE={QuoteForCmd(temp)}\r\nset PID={Process.GetCurrentProcess().Id}\r\n:wait\r\ntasklist /FI \"PID eq %PID%\" | findstr /C:\"%PID%\" >NUL\r\nif not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n:replace\r\ndel /f /q \"%TARGET%\" >NUL 2>&1\r\nif exist \"%TARGET%\" (timeout /t 1 /nobreak >NUL & goto replace)\r\nmove /y \"%NEWFILE%\" \"%TARGET%\" >NUL 2>&1\r\nif not exist \"%TARGET%\" exit /b 1\r\nstart \"\" \"%TARGET%\"\r\ndel /f /q \"%~f0\" >NUL 2>&1\r\n");
            Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c start \"\" /min \"{script}\"", UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            Application.Exit();
        }
        catch (Exception ex)
        {
            VoidEraseLogger.Error("Update check or installation failed.", ex);
            if (interactive) MessageBox.Show(this, L.T("Güncelleme başarısız oldu.\n\n" + ex.Message, "Update failed.\n\n" + ex.Message), L.T("Güncelleme", "Update"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            updateCheckRunning = false;
            if (!IsDisposed) { versionLabel.Enabled = true; versionLabel.Text = original; }
        }
    }

    private static string QuoteForCmd(string value) => value.Replace("\"", "\"\"");

    private void SetProgress(int value)
    {
        int percent = CryptoCompat.Clamp(value, 0, 100);
        progressFill.Width = (int)Math.Round(progressTrack.Width * percent / 100d);
        progressFill.Left = 0;
    }

public void ReportProgress(long processed, long total, TimeSpan elapsed)
{
    if (InvokeRequired)
    {
        BeginInvoke(() => ReportProgress(processed, total, elapsed));
        return;
    }

    int percent = total == 0
        ? 100
        : (int)CryptoCompat.Clamp(
            processed * 100L / total,
            0,
            100);

    SetProgress(percent);

    double seconds = Math.Max(elapsed.TotalSeconds, 0.001);
    double mbps = processed / 1024d / 1024d / seconds;

    long remaining = Math.Max(0, total - processed);

    double remainingSeconds =
        processed > 0
            ? remaining / (processed / seconds)
            : 0;

    statusLabel.Text =
        L.T(
            $"Siliniyor... {percent}%",
            $"Erasing... {percent}%");

    detailLabel.Text =
        L.T(
            $"{FormatSize(processed)} / {FormatSize(total)}   •   " +
            $"{mbps:0.0} MB/sn   •   Kalan: {FormatTime(remainingSeconds)}",
            $"{FormatSize(processed)} / {FormatSize(total)}   •   " +
            $"{mbps:0.0} MB/s   •   Remaining: {FormatTime(remainingSeconds)}");
}

  public void ReportValidation(long current, long total, TimeSpan elapsed)
{
    if (InvokeRequired)
    {
        BeginInvoke(() => ReportValidation(current, total, elapsed));
        return;
    }

    int percent = total == 0
        ? 100
        : (int)CryptoCompat.Clamp(
            current * 100L / total,
            0,
            100);

    SetProgress(percent);

    double seconds = Math.Max(elapsed.TotalSeconds, 0.001);
    double chunksPerSecond = current / seconds;
    long remaining = Math.Max(0, total - current);

    double remainingSeconds =
        current > 0
            ? remaining / chunksPerSecond
            : 0;

    statusLabel.Text =
        L.T(
            $"Doğrulanıyor... {percent}%",
            $"Verifying... {percent}%");

    detailLabel.Text =
        L.T(
            $"{current:N0} / {total:N0} parça   •   " +
            $"{chunksPerSecond:0.0} parça/sn   •   " +
            $"Kalan: {FormatTime(remainingSeconds)}",
            $"{current:N0} / {total:N0} chunks   •   " +
            $"{chunksPerSecond:0.0} chunks/s   •   " +
            $"Remaining: {FormatTime(remainingSeconds)}");
}

    public void ReportFinalizing()
    {
        if (InvokeRequired) { BeginInvoke(ReportFinalizing); return; }
        SetProgress(100);
        statusLabel.Text = L.T("Sonlandırılıyor...", "Finalizing...");
        detailLabel.Text = L.T("Doğrulama tamamlandı.", "Verification completed.");
    }

    public void ThrowIfCancellationRequested() => cts?.Token.ThrowIfCancellationRequested();

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" }; double value = bytes; int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.##} {units[i]}";
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--";
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1) return L.T($"{(int)t.TotalHours} sa {t.Minutes:00} dk", $"{(int)t.TotalHours} hr {t.Minutes:00} min");
        if (t.TotalMinutes >= 1) return L.T($"{t.Minutes} dk {t.Seconds:00} sn", $"{t.Minutes} min {t.Seconds:00} sec");
        return L.T($"{t.Seconds} sn", $"{t.Seconds} sec");
    }

    private void ShowOperationSummary(OperationResult result)
    {
        using var dlg = new OperationSummaryForm(result, L.English);
        dlg.ShowDialog(this);
    }
}

internal sealed class SettingsForm : Form
{
    private readonly CheckBox confirm = new();
    private readonly CheckBox autoUpdate = new();
    private readonly CheckBox protectSystem = new();
    private readonly CheckBox protectSystemDrive = new();
    private readonly CheckBox skipReparsePoints = new();
    private readonly CheckBox keepLogs = new();
    private readonly ComboBox language = new();

    private readonly ListBox protectedPaths = new();
    private readonly Button addProtectedPath = new();
    private readonly Button removeProtectedPath = new();

    public SettingsForm()
    {
        Text = L.T("Ayarlar", "Settings");
        ClientSize = new Size(520, 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(244, 247, 250);

        Label title = new()
        {
            Text = L.T("VoidErase Ayarları", "VoidErase Settings"),
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 42, 52)
        };
        title.SetBounds(24, 16, 450, 28);

        Label langLabel = new()
        {
            Text = L.T("Dil", "Language"),
            ForeColor = Color.FromArgb(101, 115, 130)
        };
        langLabel.AutoSize = true;
        langLabel.Location = new Point(24, 52);

        language.SetBounds(langLabel.Right + 8, 49, 85, 28);
        language.DropDownStyle = ComboBoxStyle.DropDownList;
        language.Items.AddRange(new object[] { "Türkçe", "English" });
        language.SelectedIndex = L.Turkish ? 0 : 1;

        confirm.Text = L.T(
            "Silmeden önce onay iste",
            "Ask for confirmation before erasing");
        confirm.Checked = L.ConfirmBeforeErase;
        confirm.SetBounds(24, 88, 470, 24);
		
var hidden = new CheckBox
{
    Text = L.T(
        "Gizli dosyaları sil",
        "Delete hidden files"),
    Checked = L.DeleteHiddenFiles,
    AutoSize = true
};

hidden.SetBounds(24, 116, 470, 24);

        autoUpdate.Text = L.T(
            "Başlangıçta güncellemeleri kontrol et",
            "Check for updates at startup");
        autoUpdate.Checked = L.AutoUpdate;
        autoUpdate.SetBounds(24, 144, 470, 24);

        protectSystem.Text = L.T(
            "Windows sistem klasörlerini koru",
            "Protect Windows system folders");
        protectSystem.Checked = VoidEraseSettings.ProtectSystemPaths;
        protectSystem.SetBounds(24, 172, 470, 24);

        protectSystemDrive.Text = L.T(
            "Sistem sürücüsü kökünü koru (örn. C:\\)",
            "Protect system drive root (e.g. C:\\)");
        protectSystemDrive.Checked = VoidEraseSettings.ProtectSystemDrive;
        protectSystemDrive.SetBounds(24, 200, 470, 24);

skipReparsePoints.Text = L.T(
    "Junction / symlink öğelerini atla ve devam et",
    "Skip junction / symlink items and continue");
skipReparsePoints.Checked = VoidEraseSettings.SkipReparsePoints;
skipReparsePoints.SetBounds(24, 228, 470, 24);


Label protectedLabel = new()
        {
            Text = L.T(
                "Kullanıcı korumalı yolları",
                "User protected paths"),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 69, 82)
        };
        protectedLabel.SetBounds(24, 256, 350, 22);
        protectedPaths.SetBounds(24, 282, 360, 82);
        protectedPaths.HorizontalScrollbar = true;
        protectedPaths.SelectionMode = SelectionMode.One;

        foreach (string path in VoidEraseSettings.ProtectedPaths)
            protectedPaths.Items.Add(path);

        addProtectedPath.Text = L.T("Ekle", "Add");
        addProtectedPath.SetBounds(394, 282, 100, 32);
        addProtectedPath.Click += (_, _) => AddProtectedPath();

        removeProtectedPath.Text = L.T("Kaldır", "Remove");
        removeProtectedPath.SetBounds(394, 320, 100, 32);
        removeProtectedPath.Click += (_, _) => RemoveProtectedPath();

        keepLogs.Text = L.T(
            "İşlem günlüklerini tut",
            "Keep operation logs");
        keepLogs.Checked = VoidEraseSettings.KeepLogs;
        keepLogs.SetBounds(24, 378, 470, 24);

        Button ok = new()
        {
            Text = "OK",
            DialogResult = DialogResult.OK
        };
        ok.SetBounds(311, 426, 80, 32);

        Button cancel = new()
        {
            Text = L.T("İptal", "Cancel"),
            DialogResult = DialogResult.Cancel
        };
        cancel.SetBounds(401, 426, 95, 32);

        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[]
        {
            title,
            langLabel,
            language,
            confirm,
            hidden,
            autoUpdate,
            protectSystem,
            protectSystemDrive,
            skipReparsePoints,
            protectedLabel,
            protectedPaths,
            addProtectedPath,
            removeProtectedPath,
            keepLogs,
            ok,
            cancel
        });

        FormClosing += (_, _) =>
        {
            if (DialogResult != DialogResult.OK)
                return;

            L.SetLanguage(language.SelectedIndex == 0);

			VoidEraseSettings.DeleteHiddenFiles = hidden.Checked;
            L.SaveSettings(
				confirm.Checked,
				autoUpdate.Checked,
				hidden.Checked);

            VoidEraseSettings.ProtectSystemPaths =
                protectSystem.Checked;

            VoidEraseSettings.ProtectSystemDrive =
                protectSystemDrive.Checked;

            VoidEraseSettings.SkipReparsePoints =
                skipReparsePoints.Checked;

            VoidEraseSettings.KeepLogs =
                keepLogs.Checked;

            VoidEraseSettings.SetProtectedPaths(
                protectedPaths.Items
                    .Cast<string>()
                    .ToArray());

            Program.UpdateContextMenuLanguage();
        };
    }

    private void AddProtectedPath()
    {
        using FolderBrowserDialog dlg = new()
        {
            Description = L.T(
                "Korunacak klasörü seçin",
                "Select a folder to protect")
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        string path = dlg.SelectedPath;

        foreach (string existing in protectedPaths.Items.Cast<string>())
        {
            if (string.Equals(
                    existing,
                    path,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        protectedPaths.Items.Add(path);
        protectedPaths.SelectedIndex =
            protectedPaths.Items.Count - 1;
    }

    private void RemoveProtectedPath()
    {
        int index = protectedPaths.SelectedIndex;

        if (index < 0)
            return;

        protectedPaths.Items.RemoveAt(index);
    }
}

internal sealed class HistoryForm : Form
{
    private readonly ListView list = new();
    private readonly ComboBox filter = new();

    private readonly Label totalValue = new();
    private readonly Label successValue = new();
    private readonly Label failedValue = new();
    private readonly Label skippedValue = new();

    private string[] allHistory = Array.Empty<string>();

    private static readonly Color BackgroundColor =
        Color.FromArgb(244, 247, 250);

    private static readonly Color CardColor =
        Color.White;

    private static readonly Color CardBorder =
        Color.FromArgb(214, 222, 231);

    private static readonly Color TextPrimary =
        Color.FromArgb(31, 42, 52);

    private static readonly Color TextSecondary =
        Color.FromArgb(101, 115, 130);

    private static readonly Color Accent =
        Color.FromArgb(25, 150, 220);

    private static readonly Color Success =
        Color.FromArgb(30, 145, 88);

    private static readonly Color Failed =
        Color.FromArgb(211, 63, 63);

    private static readonly Color Skipped =
        Color.FromArgb(190, 130, 35);

    public HistoryForm()
    {
        Text = L.T("İşlem Geçmişi", "Operation History");
        ClientSize = new Size(760, 530);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9F);
        BackColor = BackgroundColor;

        Label title = new()
        {
            Text = L.T(
                "VoidErase İşlem Geçmişi",
                "VoidErase Operation History"),
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = TextPrimary
        };
        title.SetBounds(24, 18, 470, 30);

        Label subtitle = new()
        {
            Text = L.T(
                "Silme işlemlerinizin geçmişini görüntüleyin.",
                "View your file erasure history."),
            ForeColor = TextSecondary
        };
        subtitle.SetBounds(25, 47, 470, 20);

        filter.SetBounds(575, 20, 161, 30);
        filter.DropDownStyle = ComboBoxStyle.DropDownList;
        filter.BackColor = CardColor;
        filter.ForeColor = TextPrimary;

        filter.Items.AddRange(new object[]
        {
            L.T("Tümü", "All"),
            L.T("Başarılı", "Successful"),
            L.T("Başarısız", "Failed"),
            L.T("Atlandı", "Skipped")
        });

        filter.SelectedIndex = 0;
        filter.SelectedIndexChanged += (_, _) => ApplyFilter();

        CreateStatCard(
            24,
            L.T("TOPLAM", "TOTAL"),
            totalValue,
            TextSecondary);

        CreateStatCard(
            202,
            L.T("BAŞARILI", "SUCCESSFUL"),
            successValue,
            Success);

        CreateStatCard(
            380,
            L.T("BAŞARISIZ", "FAILED"),
            failedValue,
            Failed);

        CreateStatCard(
            558,
            L.T("ATLANDI", "SKIPPED"),
            skippedValue,
            Skipped);

        list.SetBounds(24, 152, 712, 315);
        list.View = View.Details;
        list.FullRowSelect = true;
        list.GridLines = false;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.BackColor = CardColor;
        list.ForeColor = TextPrimary;

        list.Columns.Add(
            L.T("Tarih", "Date"),
            125);

        list.Columns.Add(
            L.T("Durum", "Status"),
            105);

        list.Columns.Add(
            L.T("Dosya", "File"),
            300);

        list.Columns.Add(
            L.T("Boyut", "Size"),
            75);

        list.Columns.Add(
            L.T("Doğrulama", "Verification"),
            100);

        Button clear = new()
        {
            Text = L.T(
                "Geçmişi Temizle",
                "Clear History")
        };

        clear.SetBounds(24, 490, 145, 32);
        StyleButton(
            clear,
            CardColor,
            TextPrimary,
            false);

        clear.FlatAppearance.BorderColor = CardBorder;
        clear.Click += (_, _) => ClearHistory();

        Button close = new()
        {
            Text = L.T("Kapat", "Close"),
            DialogResult = DialogResult.Cancel
        };

        close.SetBounds(636, 490, 100, 32);
        StyleButton(
            close,
            CardColor,
            TextPrimary,
            false);

        close.FlatAppearance.BorderColor = CardBorder;

        CancelButton = close;

        Controls.AddRange(new Control[]
        {
            title,
            subtitle,
            filter,
            list,
            clear,
            close
        });
list.DoubleClick += (_, _) => ShowSelectedDetails();
        LoadHistory();
    }

    private void CreateStatCard(
        int x,
        string titleText,
        Label value,
        Color valueColor)
    {
        Panel card = new()
        {
            BackColor = CardColor,
            BorderStyle = BorderStyle.FixedSingle
        };

        card.SetBounds(x, 78, 160, 58);

        Label title = new()
        {
            Text = titleText,
            Font = new Font(
                "Segoe UI",
                7.5F,
                FontStyle.Bold),
            ForeColor = TextSecondary
        };

        title.SetBounds(12, 7, 136, 16);

        value.Text = "0";
        value.Font = new Font(
            "Segoe UI",
            16F,
            FontStyle.Bold);
        value.ForeColor = valueColor;
        value.SetBounds(12, 23, 136, 29);

        card.Controls.Add(title);
        card.Controls.Add(value);

        Controls.Add(card);
    }

    private void LoadHistory()
    {
        allHistory = HistoryStore.ReadAll();

        UpdateStatistics();
        ApplyFilter();
    }

    private void UpdateStatistics()
    {
        int total = 0;
        int success = 0;
        int failed = 0;
        int skipped = 0;

        foreach (string line in allHistory)
        {
            string[] parts = line.Split('|');

            if (parts.Length < 5)
                continue;

            total++;

            if (parts[1].Equals(
                    "SUCCESS",
                    StringComparison.OrdinalIgnoreCase))
            {
                success++;
            }
            else if (parts[1].Equals(
                         "FAILED",
                         StringComparison.OrdinalIgnoreCase))
            {
                failed++;
            }
            else if (parts[1].Equals(
                         "SKIPPED",
                         StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
            }
        }

        totalValue.Text = total.ToString();
        successValue.Text = success.ToString();
        failedValue.Text = failed.ToString();
        skippedValue.Text = skipped.ToString();
    }

    private void ApplyFilter()
    {
        list.Items.Clear();

        int selectedFilter = filter.SelectedIndex;

        foreach (string line in allHistory.Reverse())
        {
            string[] parts = line.Split('|');

            if (parts.Length < 5)
                continue;

            string status = parts[1];

            bool include =
                selectedFilter == 0 ||
                (selectedFilter == 1 &&
                 status.Equals(
                     "SUCCESS",
                     StringComparison.OrdinalIgnoreCase)) ||
                (selectedFilter == 2 &&
                 status.Equals(
                     "FAILED",
                     StringComparison.OrdinalIgnoreCase)) ||
                (selectedFilter == 3 &&
                 status.Equals(
                     "SKIPPED",
                     StringComparison.OrdinalIgnoreCase));

            if (!include)
                continue;

            ListViewItem item = new(parts[0]);

            string displayStatus =
                status.Equals(
                    "SUCCESS",
                    StringComparison.OrdinalIgnoreCase)
                    ? L.T("BAŞARILI", "SUCCESS")
                    : status.Equals(
                        "FAILED",
                        StringComparison.OrdinalIgnoreCase)
                        ? L.T("BAŞARISIZ", "FAILED")
                        : status.Equals(
                            "SKIPPED",
                            StringComparison.OrdinalIgnoreCase)
                            ? L.T("ATLANDI", "SKIPPED")
                            : status;

            string verification =
                parts[4].Equals(
                    "VERIFIED",
                    StringComparison.OrdinalIgnoreCase)
                    ? L.T("DOĞRULANDI", "VERIFIED")
                    : L.T(
                        "DOĞRULANMADI",
                        "NOT VERIFIED");

            item.SubItems.Add(displayStatus);
            item.SubItems.Add(parts[2]);
            item.SubItems.Add(FormatSize(parts[3]));
            item.SubItems.Add(verification);

            if (status.Equals(
                    "SUCCESS",
                    StringComparison.OrdinalIgnoreCase))
            {
                item.ForeColor = Success;
            }
            else if (status.Equals(
                         "FAILED",
                         StringComparison.OrdinalIgnoreCase))
            {
                item.ForeColor = Failed;
            }
            else if (status.Equals(
                         "SKIPPED",
                         StringComparison.OrdinalIgnoreCase))
            {
                item.ForeColor = Skipped;
            }

            list.Items.Add(item);
        }
    }

    private static string FormatSize(string value)
    {
        if (!long.TryParse(value, out long bytes))
            return value;

        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";

        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";

        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

private void ShowSelectedDetails()
{
    if (list.SelectedItems.Count == 0)
        return;

    ListViewItem item = list.SelectedItems[0];

    if (item.SubItems.Count < 5)
        return;

    string date = item.SubItems[0].Text;
    string status = item.SubItems[1].Text;
    string file = item.SubItems[2].Text;
    string size = item.SubItems[3].Text;
    string verification = item.SubItems[4].Text;

    string fullPath = "Tam dosya yolu bu kayıt için saklanmamış.";

    int index = list.SelectedItems[0].Index;

    IEnumerable<string> records = allHistory.Reverse();

    string[] matchingRecords = records
        .Where(line =>
        {
            string[] parts = line.Split('|');

            return parts.Length >= 5 &&
                   parts[0] == date &&
                   parts[2] == file;
        })
        .ToArray();

    if (matchingRecords.Length > 0)
    {
        string[] parts = matchingRecords[0].Split('|');

        if (parts.Length >= 6 &&
            !string.IsNullOrWhiteSpace(parts[5]))
        {
            fullPath = parts[5];
        }
    }

    using OperationDetailForm dlg = new(
        date,
        status,
        file,
        size,
        verification,
        fullPath);

    dlg.ShowDialog(this);
}

    private void ClearHistory()
    {
        DialogResult answer = MessageBox.Show(
            this,
            L.T(
                "Tüm işlem geçmişi silinsin mi?",
                "Delete all operation history?"),
            L.T(
                "Geçmişi Temizle",
                "Clear History"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
            return;

        string historyPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VoidErase",
            "history.log");

        try
        {
            if (File.Exists(historyPath))
                File.Delete(historyPath);

            allHistory = Array.Empty<string>();

            UpdateStatistics();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                L.T("Hata", "Error"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void StyleButton(
        Button button,
        Color backColor,
        Color foreColor,
        bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor =
            accent
                ? backColor
                : CardBorder;

        button.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(242, 245, 248);

        button.FlatAppearance.MouseDownBackColor =
            Color.FromArgb(238, 242, 246);

        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
    }
}
internal sealed class OperationDetailForm : Form
{
    public OperationDetailForm(
        string date,
        string status,
        string file,
        string size,
        string verification,
        string fullPath)
    {
        Text = L.T(
            "İşlem Detayı",
            "Operation Details");

        ClientSize = new Size(560, 365);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(244, 247, 250);

        Label title = new()
        {
            Text = L.T(
                "VoidErase İşlem Detayı",
                "VoidErase Operation Details"),
            Font = new Font(
                "Segoe UI",
                14F,
                FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 42, 52)
        };
        title.SetBounds(24, 20, 500, 30);

        Panel card = new()
        {
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        card.SetBounds(24, 65, 512, 220);

        AddRow(
            card,
            L.T("Tarih", "Date"),
            date,
            15,
            15);

        AddRow(
            card,
            L.T("Durum", "Status"),
            status,
            15,
            55);

        AddRow(
            card,
            L.T("Dosya", "File"),
            file,
            15,
            95);

        AddRow(
            card,
            L.T("Boyut", "Size"),
            size,
            15,
            135);

        AddRow(
            card,
            L.T("Doğrulama", "Verification"),
            verification,
            15,
            175);

        Label pathTitle = new()
        {
            Text = L.T(
                "Tam Dosya Yolu",
                "Full File Path"),
            Font = new Font(
                "Segoe UI",
                8.5F,
                FontStyle.Bold),
            ForeColor = Color.FromArgb(101, 115, 130)
        };
        pathTitle.SetBounds(24, 300, 120, 20);

        TextBox pathBox = new()
        {
            Text = fullPath,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(31, 42, 52),
            AutoSize = false
        };
        pathBox.SetBounds(145, 296, 391, 28);

        Button close = new()
        {
            Text = L.T("Kapat", "Close"),
            DialogResult = DialogResult.Cancel
        };

        close.SetBounds(436, 330, 100, 30);

        close.FlatStyle = FlatStyle.Flat;
        close.FlatAppearance.BorderSize = 1;
        close.FlatAppearance.BorderColor =
            Color.FromArgb(214, 222, 231);

        close.BackColor = Color.White;
        close.ForeColor =
            Color.FromArgb(31, 42, 52);

        close.UseVisualStyleBackColor = false;
        close.Cursor = Cursors.Hand;

        CancelButton = close;

        Controls.AddRange(new Control[]
        {
            title,
            card,
            pathTitle,
            pathBox,
            close
        });
    }

    private static void AddRow(
        Panel panel,
        string caption,
        string value,
        int x,
        int y)
    {
        Label captionLabel = new()
        {
            Text = caption,
            Font = new Font(
                "Segoe UI",
                8.5F,
                FontStyle.Bold),
            ForeColor =
                Color.FromArgb(101, 115, 130)
        };

        captionLabel.SetBounds(
            x,
            y,
            110,
            24);

        Label valueLabel = new()
        {
            Text = value,
            Font = new Font(
                "Segoe UI",
                9F,
                FontStyle.Bold),
            ForeColor =
                Color.FromArgb(31, 42, 52),
            AutoEllipsis = true
        };

        valueLabel.SetBounds(
            x + 125,
            y,
            350,
            24);

        panel.Controls.Add(captionLabel);
        panel.Controls.Add(valueLabel);
    }
}
internal static class HistoryStore
{
    private static readonly object Sync = new();
    private static readonly List<string> Pending = new();
    private const int FlushThreshold = 16;

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VoidErase",
            "history.log");

    public static void Append(string path, long size, string status, bool verified = false)
    {
        try
        {
            lock (Sync)
            {
                string name = Path.GetFileName(path);
                string verification = verified ? "VERIFIED" : "NOT_VERIFIED";
                QueueLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{status}|{name}|{size}|{verification}|{path}\n");
            }
        }
        catch { }
    }

    public static void AppendBatch(string status, int count, bool verified = false)
    {
        try
        {
            lock (Sync)
            {
                string verification = verified ? "VERIFIED" : "NOT_VERIFIED";
                QueueLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{status}|{count} files|0|{verification}\n");
            }
        }
        catch { }
    }

    public static void FlushPending()
    {
        lock (Sync)
        {
            FlushPendingUnsafe();
        }
    }

    public static string[] ReadAll()
    {
        try
        {
            lock (Sync)
            {
                FlushPendingUnsafe();
                if (!File.Exists(FilePath)) return Array.Empty<string>();
                return File.ReadAllLines(FilePath);
            }
        }
        catch { return Array.Empty<string>(); }
    }

    private static void QueueLine(string line)
    {
        Pending.Add(line);
        if (Pending.Count >= FlushThreshold) FlushPendingUnsafe();
    }

    private static void FlushPendingUnsafe()
    {
        if (Pending.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.AppendAllText(FilePath, string.Concat(Pending), Encoding.UTF8);
        Pending.Clear();
    }
}
internal static class ShellRefresh
{
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void Notify()
    {
        SHChangeNotify(
            0x08000000,
            0,
            IntPtr.Zero,
            IntPtr.Zero);
    }
}











using System;

internal enum UsbExecutionGateState
{
    Pass,
    Blocked,
    Error
}

internal sealed class UsbExecutionGateResult
{
    internal UsbExecutionGateState State { get; set; }
    internal string TargetPath { get; set; }
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

internal static class UsbTargetExecutionGate
{
    // FINAL USB TARGET GATE — DRY-RUN ONLY.
    // No disk is opened for writing and no destructive command is issued.
    internal static UsbExecutionGateResult Verify(
        string targetPath,
        int expectedDiskNumber,
        string expectedModel,
        string expectedSerialNumber,
        long expectedMinimumSizeBytes)
    {
        UsbExecutionGateResult result = new UsbExecutionGateResult
        {
            State = UsbExecutionGateState.Error,
            TargetPath = targetPath ?? "",
            PhysicalDrive = @"\\.\PHYSICALDRIVE" + expectedDiskNumber,
            DiskNumber = expectedDiskNumber,
            Model = "",
            SerialNumber = "",
            BusType = "",
            MediaType = "",
            DiskSizeBytes = 0,
            Reason = "",
            Scope = "No write scope established."
        };

        try
        {
            if (expectedDiskNumber < 0)
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason = "Invalid expected USB disk number.";
                return result;
            }

            FinalStorageSafetyResult finalSafety =
                FinalStorageSafetyGate.VerifyTarget(targetPath);

            if (finalSafety.Decision != FinalStorageSafetyDecision.DryRunOnly)
            {
                result.State = finalSafety.Decision == FinalStorageSafetyDecision.Blocked
                    ? UsbExecutionGateState.Blocked
                    : UsbExecutionGateState.Error;
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

            UsbTargetPreflightResult preflight =
                UsbTargetPreflight.AnalyzeDrive(targetPath);

            if (preflight == null)
            {
                result.State = UsbExecutionGateState.Error;
                result.Reason = "USB preflight returned no result.";
                return result;
            }

            result.TargetPath = preflight.DriveRoot ?? targetPath ?? "";
            result.PhysicalDrive = preflight.PhysicalDrive ?? "";
            result.DiskNumber = preflight.DiskNumber;
            result.Model = preflight.Model ?? "";
            result.SerialNumber = preflight.SerialNumber ?? "";
            result.BusType = preflight.BusType ?? "";
            result.MediaType = preflight.MediaType ?? "";
            result.DiskSizeBytes = preflight.DiskSizeBytes;
            result.IsSystem = preflight.IsSystem;
            result.IsBoot = preflight.IsBoot;
            result.IsOffline = preflight.IsOffline;
            result.IsReadOnly = preflight.IsReadOnly;

            if (preflight.State != UsbTargetPreflightState.Pass)
            {
                result.State = preflight.State == UsbTargetPreflightState.Blocked
                    ? UsbExecutionGateState.Blocked
                    : UsbExecutionGateState.Error;
                result.Reason =
                    "USB preflight did not pass: " +
                    (preflight.Reason ?? "Unknown reason.");
                return result;
            }

            if (result.DiskNumber != expectedDiskNumber)
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason =
                    "Target disk number changed. Expected PHYSICALDRIVE" +
                    expectedDiskNumber +
                    ", but Windows reports PHYSICALDRIVE" +
                    result.DiskNumber + ".";
                return result;
            }

            if (!string.Equals(result.BusType, "USB",
                StringComparison.OrdinalIgnoreCase))
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason = "Target is no longer reported as USB.";
                return result;
            }

            if (!string.IsNullOrWhiteSpace(expectedModel) &&
                !string.Equals(result.Model ?? "", expectedModel,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason =
                    "Target model changed. Expected '" +
                    expectedModel + "', current '" +
                    result.Model + "'.";
                return result;
            }

            if (!string.IsNullOrWhiteSpace(expectedSerialNumber) &&
                !string.Equals(result.SerialNumber ?? "", expectedSerialNumber,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason =
                    "Target serial number changed. The selected USB device " +
                    "does not match the approved device.";
                return result;
            }

            const long SizeReportingToleranceBytes = 1024L * 1024L;
            long minimumAcceptedSize = expectedMinimumSizeBytes;

            if (expectedMinimumSizeBytes > SizeReportingToleranceBytes)
                minimumAcceptedSize =
                    expectedMinimumSizeBytes - SizeReportingToleranceBytes;

            if (expectedMinimumSizeBytes > 0 &&
                result.DiskSizeBytes < minimumAcceptedSize)
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason =
                    "The current USB disk is smaller than the approved " +
                    "minimum size, even after the 1 MiB reporting tolerance.";
                return result;
            }

            if (result.IsSystem || result.IsBoot)
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason =
                    "The selected USB disk is reported as a system or boot disk.";
                return result;
            }

            if (result.IsOffline)
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason = "The selected USB disk is offline.";
                return result;
            }

            if (result.IsReadOnly)
            {
                result.State = UsbExecutionGateState.Blocked;
                result.Reason = "The selected USB disk is read-only.";
                return result;
            }

            result.State = UsbExecutionGateState.Pass;
            result.Scope =
                "DRY-RUN USB TARGET SCOPE: " +
                result.TargetPath + " -> " + result.PhysicalDrive +
                "; target identity, bus and safety gates passed. " +
                "No sectors will be written by this gate.";
            result.Reason =
                "USB target identity and safety gates passed. " +
                "No destructive operation was executed.";

            return result;
        }
        catch (Exception ex)
        {
            result.State = UsbExecutionGateState.Error;
            result.Reason = "USB execution gate failed: " + ex.Message;
            return result;
        }
    }
}

using System;

internal enum UsbExecutionPlanState
{
    Ready,
    Blocked,
    Error
}

internal sealed class UsbExecutionPlan
{
    internal UsbExecutionPlanState State { get; set; }
    internal string TargetPath { get; set; }
    internal string PhysicalDrive { get; set; }
    internal int DiskNumber { get; set; }
    internal string Model { get; set; }
    internal string SerialNumber { get; set; }
    internal long DiskSizeBytes { get; set; }
    internal long AddressableBytes { get; set; }
    internal string Reason { get; set; }
    internal string Scope { get; set; }
}

internal static class UsbTargetExecution
{
    // DRY-RUN EXECUTION PLANNER ONLY.
    // No deletion, disk write, format, TRIM, sanitize or destructive command.
    internal static UsbExecutionPlan BuildDryRunPlan(
        UsbExecutionGateResult gate)
    {
        UsbExecutionPlan plan = new UsbExecutionPlan
        {
            State = UsbExecutionPlanState.Error,
            TargetPath = gate == null ? "" : gate.TargetPath ?? "",
            PhysicalDrive = gate == null ? "" : gate.PhysicalDrive ?? "",
            DiskNumber = gate == null ? -1 : gate.DiskNumber,
            Model = gate == null ? "" : gate.Model ?? "",
            SerialNumber = gate == null ? "" : gate.SerialNumber ?? "",
            DiskSizeBytes = gate == null ? 0 : gate.DiskSizeBytes,
            AddressableBytes = 0,
            Scope = "No execution scope established.",
            Reason = ""
        };

        if (gate == null)
        {
            plan.State = UsbExecutionPlanState.Error;
            plan.Reason = "USB execution gate result is null.";
            return plan;
        }

        if (gate.State != UsbExecutionGateState.Pass)
        {
            plan.State = gate.State == UsbExecutionGateState.Blocked
                ? UsbExecutionPlanState.Blocked
                : UsbExecutionPlanState.Error;
            plan.Reason =
                "Execution plan blocked because the USB final safety gate did not pass. " +
                (gate.Reason ?? "");
            return plan;
        }

        // UsbTargetPreflight already establishes the Windows-reported disk size.
        // Geometry is intentionally not invented here.
        plan.AddressableBytes = gate.DiskSizeBytes;

        if (plan.AddressableBytes <= 0)
        {
            plan.State = UsbExecutionPlanState.Blocked;
            plan.Reason = "USB reported disk size is not valid.";
            return plan;
        }

        plan.State = UsbExecutionPlanState.Ready;
        plan.Scope =
            "DRY-RUN READY: target identity and safety gates passed. " +
            "Reported disk size = " +
            plan.AddressableBytes.ToString("N0") +
            " bytes. No destructive operation will be performed.";
        plan.Reason =
            "USB execution plan is READY for a separately approved execution phase. " +
            "This build performs no disk write or erase.";

        return plan;
    }
}

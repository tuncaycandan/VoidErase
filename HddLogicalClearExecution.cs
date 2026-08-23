using System;

internal enum HddLogicalClearExecutionState
{
    Ready,
    Blocked,
    Error
}

internal sealed class HddLogicalClearExecutionPlan
{
    internal HddLogicalClearExecutionState State { get; set; }
    internal HddExecutionGateResult Gate { get; set; }
    internal string TargetPath { get; set; }
    internal string Method { get; set; }
    internal string Message { get; set; }
    internal uint LogicalSectorSize { get; set; }
    internal uint PhysicalSectorSize { get; set; }
    internal long AddressableBytes { get; set; }
    internal long AlignedWriteBytes { get; set; }
    internal long BlockSizeBytes { get; set; }
    internal long BlockCount { get; set; }
    internal string VerificationPlan { get; set; }
    internal string ExecutionMode { get; set; }
}

internal static class HddLogicalClearExecution
{
    // SAFETY / DRY-RUN ONLY.
    // This coordinator deliberately does not contain or invoke any
    // destructive disk-write, overwrite, sanitize, format, TRIM or IOCTL code.
    //
    // It creates the execution plan only after the final identity gate passes.

    internal static HddLogicalClearExecutionPlan Prepare(
        string targetPath,
        int expectedDiskNumber,
        string expectedModel,
        string expectedSerialNumber,
        long expectedMinimumSizeBytes)
    {
        HddLogicalClearExecutionPlan plan =
            new HddLogicalClearExecutionPlan
            {
                State = HddLogicalClearExecutionState.Error,
                TargetPath = targetPath ?? "",
                Method = "HDD LogicalClear",
                Message = "Execution plan has not been approved."
            };

        try
        {
            HddExecutionGateResult gate =
                HddLogicalClearExecutionGate.Verify(
                    targetPath,
                    expectedDiskNumber,
                    expectedModel,
                    expectedSerialNumber,
                    expectedMinimumSizeBytes);

            plan.Gate = gate;

            if (gate == null)
            {
                plan.State = HddLogicalClearExecutionState.Error;
                plan.Message = "Final execution gate returned no result.";
                return plan;
            }

            if (gate.State != HddExecutionGateState.Pass)
            {
                plan.State = HddLogicalClearExecutionState.Blocked;
                plan.Message =
                    "Execution plan blocked by final safety gate: " +
                    gate.Reason;
                return plan;
            }

            uint logicalSector = gate.LogicalSectorSize;
            uint physicalSector = gate.PhysicalSectorSize;

            if (logicalSector == 0 || physicalSector == 0)
            {
                plan.State = HddLogicalClearExecutionState.Blocked;
                plan.Message =
                    "Execution plan blocked: valid sector geometry was not established.";
                return plan;
            }

            const long PlanningBlockSize = 1024L * 1024L;
            long blockSize = PlanningBlockSize;

            long alignment = physicalSector;
            long remainder = blockSize % alignment;
            if (remainder != 0)
                blockSize += alignment - remainder;

            long addressableBytes = gate.DiskSizeBytes;
            long alignedWriteBytes =
                addressableBytes - (addressableBytes % logicalSector);

            if (alignedWriteBytes <= 0)
            {
                plan.State = HddLogicalClearExecutionState.Blocked;
                plan.Message =
                    "Execution plan blocked: addressable disk size is not sector-aligned.";
                return plan;
            }

            long blockCount =
                (alignedWriteBytes + blockSize - 1) / blockSize;

            plan.LogicalSectorSize = logicalSector;
            plan.PhysicalSectorSize = physicalSector;
            plan.AddressableBytes = addressableBytes;
            plan.AlignedWriteBytes = alignedWriteBytes;
            plan.BlockSizeBytes = blockSize;
            plan.BlockCount = blockCount;
            plan.VerificationPlan =
                "Read-back verification is planned for every written block; " +
                "execution remains disabled in this build.";
            plan.ExecutionMode =
                "DRY-RUN ONLY — no physical-disk write engine is attached.";

            plan.State = HddLogicalClearExecutionState.Ready;
            plan.Message =
                "DRY-RUN READY: target identity, geometry and safety gates passed. " +
                "A sector-aligned execution plan was generated; no destructive operation has been executed.";

            return plan;
        }
        catch (Exception ex)
        {
            plan.State = HddLogicalClearExecutionState.Error;
            plan.Message = "Execution plan failed: " + ex.Message;
            return plan;
        }
    }
}

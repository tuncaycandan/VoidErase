using System;

internal static class HddLogicalClearExecutionTest
{
    // DRY-RUN ONLY. This helper validates plan construction against the
    // currently known non-system HDD test target. It performs no writes.
    internal static HddLogicalClearExecutionPlan TestDDrive()
    {
        HddLogicalClearExecutionPlan plan =
            HddLogicalClearExecution.Prepare(
                @"D:\",
                0,
                "ST3000VX000-1ES166",
                "W50108VF",
                3000000000000L);

        if (plan == null)
            throw new InvalidOperationException("Execution plan was null.");

        if (plan.State != HddLogicalClearExecutionState.Ready)
            throw new InvalidOperationException(
                "Execution plan did not reach READY: " + plan.Message);

        if (plan.LogicalSectorSize == 0 || plan.PhysicalSectorSize == 0)
            throw new InvalidOperationException("Sector geometry was not established.");

        if (plan.AlignedWriteBytes <= 0 || plan.BlockCount <= 0)
            throw new InvalidOperationException("A valid aligned write plan was not generated.");

        return plan;
    }
}

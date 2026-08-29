using System;

namespace VoidErase;

internal static class SafetyScenarioTests
{
    internal static void RunAll()
    {
        TestSystemDiskIsBlocked();
        TestBootDiskIsBlocked();
        TestIdentityMismatchIsRejected();
        TestIdentityComparisonReportsMismatch();
        TestDryRunNeverAuthorizesPhysicalWrite();
    }

    private static void TestSystemDiskIsBlocked()
    {
        SanitizationIdentitySnapshot identity = new SanitizationIdentitySnapshot
        {
            PhysicalDrive = @"\\.\PHYSICALDRIVE0",
            DiskNumber = "0",
            Model = "Test Disk",
            SerialNumber = "TEST-SYSTEM",
            SizeBytes = 100,
            IsSystemDisk = true
        };
        SafeProviderPlan plan = SafeProviderFactory.CreateDryRunPlan(identity, false);
        Assert(plan.DryRunOnly && !plan.PhysicalWriteAuthorized, "Sistem diski dry-run dışında bırakılamaz.");
    }

    private static void TestBootDiskIsBlocked()
    {
        SanitizationIdentitySnapshot identity = new SanitizationIdentitySnapshot
        {
            PhysicalDrive = @"\\.\PHYSICALDRIVE1",
            DiskNumber = "1",
            Model = "Test Disk",
            SerialNumber = "TEST-BOOT",
            SizeBytes = 100,
            IsBootDisk = true
        };
        SafeProviderPlan plan = SafeProviderFactory.CreateDryRunPlan(identity, false);
        Assert(plan.DryRunOnly && !plan.PhysicalWriteAuthorized, "Boot diski dry-run dışında bırakılamaz.");
    }

    private static void TestIdentityMismatchIsRejected()
    {
        SanitizationIdentitySnapshot before = new SanitizationIdentitySnapshot
        {
            PhysicalDrive = @"\\.\PHYSICALDRIVE3", DiskNumber = "3", Model = "USB", SerialNumber = "A", MediaType = "USB", BusType = "USB", SizeBytes = 100
        };
        SanitizationIdentitySnapshot after = new SanitizationIdentitySnapshot
        {
            PhysicalDrive = @"\\.\PHYSICALDRIVE3", DiskNumber = "3", Model = "USB", SerialNumber = "B", MediaType = "USB", BusType = "USB", SizeBytes = 100
        };
        Assert(!before.Matches(after), "Kimlik değişikliği kabul edilmemelidir.");
    }

    private static void TestIdentityComparisonReportsMismatch()
    {
        SanitizationIdentitySnapshot before = new SanitizationIdentitySnapshot
        {
            PhysicalDrive = @"\\.\PHYSICALDRIVE3", DiskNumber = "3", Model = "USB", SerialNumber = "A", MediaType = "USB", BusType = "USB", SizeBytes = 100
        };
        SanitizationIdentitySnapshot after = new SanitizationIdentitySnapshot
        {
            PhysicalDrive = @"\\.\PHYSICALDRIVE3", DiskNumber = "3", Model = "USB", SerialNumber = "B", MediaType = "USB", BusType = "USB", SizeBytes = 100
        };
        IdentityComparisonResult result = MediaIdentityValidation.Compare(before, after, false);
        Assert(!result.Match && result.Status == "Başarısız", "Kimlik uyuşmazlığı başarısız raporlanmalıdır.");
    }

    private static void TestDryRunNeverAuthorizesPhysicalWrite()
    {
        DryRunSanitizationProvider provider = new DryRunSanitizationProvider();
        string result = provider.PrepareDryRun("E:\\");
        Assert(result.IndexOf("DRY-RUN", StringComparison.OrdinalIgnoreCase) >= 0, "Dry-run sonucu açıkça belirtilmelidir.");
        Assert(!provider.PhysicalWriteAuthorized && provider.ProviderVersion == "1.4.0", "Dry-run sağlayıcısı fiziksel yazmayı yetkilendirmemelidir.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

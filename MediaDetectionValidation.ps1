$ErrorActionPreference = 'Stop'

Write-Host '=== VoidErase Media Detection Validation ===' -ForegroundColor Cyan
Write-Host ''

Write-Host '--- Windows disk inventory ---' -ForegroundColor Yellow
Get-Disk |
    Format-Table Number,FriendlyName,Size,BusType,PartitionStyle,IsBoot,IsSystem -Auto

Write-Host ''
Write-Host '--- Physical media inventory ---' -ForegroundColor Yellow
Get-PhysicalDisk |
    Format-Table DeviceId,FriendlyName,SerialNumber,MediaType,BusType,HealthStatus,OperationalStatus -Auto

Write-Host ''
Write-Host '--- Win32 physical disk inventory ---' -ForegroundColor Yellow
Get-CimInstance Win32_DiskDrive |
    Format-Table DeviceID,Model,SerialNumber,Size,MediaType,InterfaceType -Auto

Write-Host ''
Write-Host '--- Volume -> physical disk mapping ---' -ForegroundColor Yellow
Get-Partition |
    Where-Object DriveLetter |
    Format-Table DiskNumber,PartitionNumber,DriveLetter,Size,Type -Auto

Write-Host ''
Write-Host '--- VoidErase media tests ---' -ForegroundColor Yellow
$exe = Join-Path $PSScriptRoot 'bin\Release\VoidErase.exe'
if (Test-Path $exe) {
    Get-Disk | Sort-Object Number | ForEach-Object {
        Write-Host "Testing PHYSICALDRIVE$($_.Number) ..."
        & $exe --media-test-disk $_.Number
    }
} else {
    Write-Warning "Release executable not found: $exe"
}

Write-Host ''
Write-Host 'Validation is read-only. No erase/sanitize/write command is issued by this script.' -ForegroundColor Green

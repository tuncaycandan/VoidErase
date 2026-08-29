$ErrorActionPreference = 'Stop'
$oldExe = 'D:\ahk\VoidErase\bin\Release\VoidErase.exe'
$newExe = 'D:\ahk\VoidErase-v1.4.0-SOURCE\bin\Release\VoidErase.exe'
$root = Join-Path $env:TEMP ('VoidErasePerf-' + [guid]::NewGuid().ToString('N'))
$regPath = 'HKCU:\Software\VoidErase'
$backup = @{}
foreach ($name in @('AskBeforeDeletion','CheckUpdatesOnStartup','KeepLogs','ProtectSystemPaths','ProtectSystemDrive','SkipReparsePoints')) {
    try { $backup[$name] = (Get-ItemProperty -Path $regPath -Name $name -ErrorAction Stop).$name } catch { $backup[$name] = $null }
}

function Set-TestSettings {
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name AskBeforeDeletion -Type DWord -Value 0
    Set-ItemProperty -Path $regPath -Name CheckUpdatesOnStartup -Type DWord -Value 0
    Set-ItemProperty -Path $regPath -Name KeepLogs -Type DWord -Value 1
    Set-ItemProperty -Path $regPath -Name ProtectSystemPaths -Type DWord -Value 1
    Set-ItemProperty -Path $regPath -Name ProtectSystemDrive -Type DWord -Value 1
    Set-ItemProperty -Path $regPath -Name SkipReparsePoints -Type DWord -Value 1
}

function New-FixedFile([string]$path, [long]$bytes) {
    $buffer = New-Object byte[] (1024 * 1024)
    for ($i = 0; $i -lt $buffer.Length; $i++) { $buffer[$i] = [byte](($i * 31 + 17) % 251) }
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $remaining = $bytes
        while ($remaining -gt 0) {
            $count = [int][Math]::Min($remaining, $buffer.Length)
            $stream.Write($buffer, 0, $count)
            $remaining -= $count
        }
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}

function New-TestSet([string]$dir, [string]$kind) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    switch ($kind) {
        'single-100mb' { New-FixedFile (Join-Path $dir 'payload.bin') (100MB) }
        'many-100kb' {
            for ($i = 0; $i -lt 1000; $i++) { New-FixedFile (Join-Path $dir (('file-{0:D4}.bin' -f $i))) (100KB) }
        }
        'tiny-10kb' {
            for ($i = 0; $i -lt 1000; $i++) { New-FixedFile (Join-Path $dir (('file-{0:D4}.bin' -f $i))) (10KB) }
        }
    }
}

function Run-Case([string]$label, [string]$exe, [string]$kind) {
    $dir = Join-Path $root (($label + '-' + $kind))
    New-TestSet $dir $kind
    $bytesBefore = (Get-ChildItem $dir -File -Recurse | Measure-Object Length -Sum).Sum
    $countBefore = (Get-ChildItem $dir -File -Recurse).Count
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $exe -ArgumentList @('--destroy', $dir) -PassThru
    $shell = New-Object -ComObject WScript.Shell
    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 500
        if ($sw.Elapsed.TotalSeconds -gt 600) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            throw "TIMEOUT: $label / $kind"
        }
        try {
            if ($shell.AppActivate($proc.Id)) { $shell.SendKeys('{ENTER}') }
        } catch { }
    }
    $sw.Stop()
    $remaining = if (Test-Path $dir) { (Get-ChildItem $dir -File -Recurse -ErrorAction SilentlyContinue).Count } else { 0 }
    [pscustomobject]@{
        Build = $label
        Case = $kind
        Files = $countBefore
        Bytes = [int64]$bytesBefore
        Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 3)
        MBps = if ($sw.Elapsed.TotalSeconds -gt 0) { [math]::Round(($bytesBefore / 1MB) / $sw.Elapsed.TotalSeconds, 2) } else { 0 }
        ExitCode = $proc.ExitCode
        RemainingFiles = $remaining
    }
}

try {
    if (-not (Test-Path $oldExe)) { throw "Old build not found: $oldExe" }
    if (-not (Test-Path $newExe)) { throw "New build not found: $newExe" }
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    Set-TestSettings
    $results = @()
    foreach ($kind in @('single-100mb','many-100kb','tiny-10kb')) {
        $results += Run-Case 'old' $oldExe $kind
        $results += Run-Case 'new' $newExe $kind
    }
    $results | Format-Table -AutoSize
    $reportPath = 'D:\ahk\VoidErase-v1.4.0-SOURCE\perf-results.csv'
    $results | Export-Csv $reportPath -NoTypeInformation
    Write-Output ('RESULTS_CSV=' + $reportPath)
} finally {
    if ($backup.Count -gt 0) {
        New-Item -Path $regPath -Force | Out-Null
        foreach ($name in $backup.Keys) {
            if ($null -eq $backup[$name]) { Remove-ItemProperty -Path $regPath -Name $name -ErrorAction SilentlyContinue }
            else { Set-ItemProperty -Path $regPath -Name $name -Value $backup[$name] }
        }
    }
    if (Test-Path $root) { Remove-Item -Path $root -Recurse -Force -ErrorAction SilentlyContinue }
}

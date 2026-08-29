$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin\Release\VoidErase.exe'
$root = Join-Path $env:TEMP ('VoidErase-FinalSmoke-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'sample.txt') -Value 'VoidErase final smoke test'
    $process = Start-Process -FilePath $exe -ArgumentList @('--benchmark', $root) -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Benchmark exited with code $($process.ExitCode)." }
    $csv = Join-Path $root 'voiderase-benchmark-results.csv'
    if (-not (Test-Path -LiteralPath $csv)) { throw 'Benchmark CSV was not created.' }
    $rows = Import-Csv -LiteralPath $csv
    Write-Output ('ROW_COUNT=' + @($rows).Count)
    Write-Output ('VERIFIED_VALUE=' + $rows[0].verified)
    Get-Content -LiteralPath $csv
    if (@($rows).Count -ne 1 -or [string]$rows[0].verified -ne 'True') { throw 'Benchmark verification result was not true.' }
    Write-Output 'SMOKE_TEST=PASS'
    Write-Output ('CSV=' + $csv)
    Get-Content -LiteralPath $csv
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

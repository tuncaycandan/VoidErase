$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "VoidErase.Framework48.csproj"
Write-Host "Building VoidErase Framework 4.8..." -ForegroundColor Cyan
msbuild $project /t:Build /p:Configuration=Release /p:Platform=AnyCPU
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Write-Host "Build successful." -ForegroundColor Green
Write-Host (Join-Path $PSScriptRoot "bin\Release\VoidErase.exe")

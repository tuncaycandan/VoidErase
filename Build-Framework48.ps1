$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "VoidErase.Framework48.csproj"
$configuration = "Release"

if (-not (Test-Path $project)) {
    throw "Project file was not found: $project"
}

Write-Host "Building VoidErase Framework 4.8 ($configuration)..." -ForegroundColor Cyan

$msbuild = Get-Command msbuild -ErrorAction SilentlyContinue
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue

if ($msbuild) {
    Write-Host "Using MSBuild: $($msbuild.Source)" -ForegroundColor DarkGray
    & $msbuild.Source $project /t:Build /p:Configuration=$configuration /verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE."
    }
}
elseif ($dotnet) {
    Write-Host "Using dotnet CLI: $($dotnet.Source)" -ForegroundColor DarkGray
    & $dotnet.Source build $project -c $configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}
else {
    throw "Neither MSBuild nor dotnet was found. Install Visual Studio Build Tools with the .NET Framework 4.8 targeting pack."
}

$output = Join-Path $PSScriptRoot "bin\$configuration\VoidErase.exe"
if (-not (Test-Path $output)) {
    throw "Build completed but the expected output was not found: $output"
}

Write-Host "Build successful." -ForegroundColor Green
Write-Host "Output: $output"

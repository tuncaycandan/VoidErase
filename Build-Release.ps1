[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$RunTests,
    [switch]$CopyToProjectRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'VoidErase.Framework48.csproj'
$assemblyInfo = Join-Path $projectRoot 'AssemblyInfo.cs'
$configuration = 'Release'
$output = Join-Path $projectRoot 'bin\Release\VoidErase.exe'

function Fail([string]$message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

try {
    if (-not (Test-Path -LiteralPath $project)) {
        Fail "Project file was not found: $project"
    }

    $msbuild = Get-Command msbuild -ErrorAction SilentlyContinue
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $msbuild -and -not $dotnet) {
        Fail 'Neither MSBuild nor dotnet was found. Install the .NET Framework 4.8 targeting pack or Visual Studio Build Tools.'
    }

    $version = 'unknown'
    if (Test-Path -LiteralPath $assemblyInfo) {
        $versionMatch = Select-String -Path $assemblyInfo -Pattern 'AssemblyInformationalVersion\("([^"]+)"\)' | Select-Object -First 1
        if ($versionMatch) { $version = $versionMatch.Matches[0].Groups[1].Value }
    }

    Write-Host "VoidErase Release Build" -ForegroundColor Cyan
    Write-Host "Project : $project" -ForegroundColor DarkGray
    Write-Host "Version : $version" -ForegroundColor DarkGray
    Write-Host "Config  : $configuration / x64" -ForegroundColor DarkGray

    if ($Clean) {
        $bin = Join-Path $projectRoot 'bin\Release'
        $obj = Join-Path $projectRoot 'obj\Release'
        Write-Host 'Cleaning Release output...' -ForegroundColor Yellow
        if (Test-Path -LiteralPath $bin) { Remove-Item -LiteralPath $bin -Recurse -Force }
        if (Test-Path -LiteralPath $obj) { Remove-Item -LiteralPath $obj -Recurse -Force }
    }

    if ($msbuild) {
        Write-Host "Using MSBuild: $($msbuild.Source)" -ForegroundColor DarkGray
        & $msbuild.Source $project /t:Build /p:Configuration=$configuration /p:Platform=AnyCPU /verbosity:minimal
    }
    else {
        Write-Host "Using dotnet CLI: $($dotnet.Source)" -ForegroundColor DarkGray
        & $dotnet.Source build $project -c $configuration --nologo
    }

    if ($LASTEXITCODE -ne 0) {
        Fail "Build failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $output)) {
        Fail "Build completed but the expected output was not found: $output"
    }

    if ($RunTests) {
        $testProjects = @(Get-ChildItem -LiteralPath $projectRoot -Filter '*Tests.csproj' -File -ErrorAction SilentlyContinue)
        if ($testProjects.Count -eq 0) {
            Write-Host 'No separate test project found; build verification only was performed.' -ForegroundColor Yellow
        }
        else {
            foreach ($testProject in $testProjects) {
                Write-Host "Running tests: $($testProject.Name)" -ForegroundColor Cyan
                & $dotnet.Source test $testProject.FullName -c $configuration --no-restore --nologo
                if ($LASTEXITCODE -ne 0) { Fail "Tests failed with exit code $LASTEXITCODE." }
            }
        }
    }

    if ($CopyToProjectRoot) {
        $rootCopy = Join-Path $projectRoot 'VoidErase.exe'
        Copy-Item -LiteralPath $output -Destination $rootCopy -Force
        Write-Host "Copied to: $rootCopy" -ForegroundColor DarkGray
    }

    $file = Get-Item -LiteralPath $output
    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    Write-Host ''
    Write-Host 'BUILD SUCCESSFUL' -ForegroundColor Green
    Write-Host "Output : $($file.FullName)"
    Write-Host "Size   : $($file.Length) bytes"
    Write-Host "SHA256 : $hash"
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Build and validate the experimental recording branch on Windows.
# Run from a PowerShell terminal with Visual Studio Build Tools installed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'RScreenRec.csproj'
if (-not (Test-Path $project)) { throw 'RScreenRec.csproj not found.' }

$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $msbuild) {
    $candidate = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($candidate) { $msbuild = $candidate.Source }
}
if (-not $msbuild) { throw 'MSBuild not found. Install Visual Studio Build Tools with .NET Framework 4.8 targeting pack.' }

Write-Host "Building: $project"
& $msbuild $project /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed (exit $LASTEXITCODE)." }

$exe = Join-Path $root 'bin\Release\RScreenRec.exe'
if (-not (Test-Path $exe)) { throw 'Build completed without the expected executable.' }
Write-Host "Build OK: $exe"
Write-Host ''
Write-Host 'Manual checks required:'
Write-Host '1. Launch normally, record the monitor, launch again to stop, play the MP4.'
Write-Host '2. Hold Shift when launching, accept the warning, select a window.'
Write-Host '3. Cover the target with an unrelated window and display a notification.'
Write-Host '4. Resize the target and verify aspect ratio; launch again to stop.'
Write-Host '5. Repeat with a GPU-accelerated app; check for black/stale frames.'
Write-Host '6. Minimize and close the target; verify graceful failure and MP4 integrity.'
Write-Host '7. Inspect %TEMP%\screenrec.log for errors.'

# Build the Release executable and print the Windows integration-check list.
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
& $msbuild $project /t:Rebuild /restore /p:Configuration=Release /p:Platform=AnyCPU /p:PlatformTarget=x64 /p:Prefer32Bit=false /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed (exit $LASTEXITCODE)." }

$exe = Join-Path $root 'bin\Release\RBG - RScreenRec.exe'
if (-not (Test-Path $exe)) { throw 'Build completed without the expected executable.' }
Write-Host "Release x64 build OK: $exe"
Write-Host ''
Write-Host 'Manual checks required:'
Write-Host '1. Launch normally, record the monitor, launch again to stop, play the MP4.'
Write-Host '2. Hold Shift when launching, select a window, and verify WGC or the PrintWindow compatibility warning.'
Write-Host '3. Cover the target with an unrelated window and display a notification.'
Write-Host '4. Resize and move the target; verify aspect ratio and resume capture after restoring it.'
Write-Host '5. Repeat with a GPU-accelerated app; check for black/stale frames.'
Write-Host '6. Minimize the target: the last frame is held, not live; restore it and verify updates resume.'
Write-Host '7. Close target: verify graceful failure and MP4 integrity.'
Write-Host '8. Inspect %TEMP%\screenrec.log for errors and frame counts.'

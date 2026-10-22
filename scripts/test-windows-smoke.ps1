[CmdletBinding()]
param(
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'build-check.ps1'
& $buildScript

$runtimeExe = Join-Path $root 'bin\Release\RScreenRec.exe'
if (-not (Test-Path -LiteralPath $runtimeExe -PathType Leaf)) { throw 'Release executable is missing.' }
$ffmpeg = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
$ffprobe = Get-Command ffprobe.exe -ErrorAction SilentlyContinue
if (-not $ffmpeg -or -not $ffprobe) { throw 'Install FFmpeg and make ffmpeg.exe and ffprobe.exe available on PATH to run media validation.' }

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'The .NET Framework C# compiler was not found.' }

$testRoot = Join-Path $env:TEMP ('RScreenRecSmoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$resolvedTestRoot = (Resolve-Path -LiteralPath $testRoot).Path
$success = $false

try {
    Copy-Item -LiteralPath $runtimeExe -Destination (Join-Path $testRoot 'RScreenRec.exe')
    $windowHarness = Join-Path $testRoot 'WindowCaptureSmoke.exe'
    $longHarness = Join-Path $testRoot 'LongDurationWriterSmoke.exe'
    $targetHarness = Join-Path $testRoot 'SyntheticCaptureTarget.exe'
    & $compiler /nologo /target:winexe "/out:$targetHarness" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $root 'tests\SyntheticCaptureTarget.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the synthetic window target.' }
    & $compiler /nologo /target:exe "/out:$windowHarness" "/reference:$(Join-Path $testRoot 'RScreenRec.exe')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $root 'tests\WindowCaptureSmoke.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the selected-window smoke harness.' }
    & $compiler /nologo /target:exe "/out:$longHarness" "/reference:$(Join-Path $testRoot 'RScreenRec.exe')" (Join-Path $root 'tests\LongDurationWriterSmoke.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the long-duration writer harness.' }

    $captureCases = @(
        @{ Name = 'odd-landscape'; Width = 671; Height = 377 },
        @{ Name = 'portrait'; Width = 377; Height = 671 },
        @{ Name = 'panorama'; Width = 1001; Height = 311 }
    )
    $results = @()
    foreach ($case in $captureCases) {
        $video = Join-Path $testRoot ($case.Name + '.mp4')
        $harnessOutput = & $windowHarness $video $case.Width $case.Height 7 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Window capture failed for $($case.Name): $($harnessOutput -join ' ')" }
        if (-not ($harnessOutput -join ' ').Contains('backend=Windows Graphics Capture')) {
            throw "Windows Graphics Capture was not selected for $($case.Name): $($harnessOutput -join ' ')"
        }

        $probeJson = & $ffprobe.Source -v error -select_streams v:0 -show_entries 'stream=codec_name,width,height,avg_frame_rate,nb_frames,duration:format=size' -of json $video
        if ($LASTEXITCODE -ne 0) { throw "ffprobe failed for $($case.Name)." }
        $probe = ($probeJson -join [Environment]::NewLine) | ConvertFrom-Json
        $stream = $probe.streams | Select-Object -First 1
        $expectedWidth = [int]([Math]::Ceiling($case.Width / 2.0) * 2)
        $expectedHeight = [int]([Math]::Ceiling($case.Height / 2.0) * 2)
        if ($stream.codec_name -ne 'h264' -or $stream.width -ne $expectedWidth -or $stream.height -ne $expectedHeight) {
            throw "Unexpected encoded format for $($case.Name): $($stream | ConvertTo-Json -Compress)"
        }
        if ($stream.avg_frame_rate -ne '30/1' -or [int64]$stream.nb_frames -lt 180) {
            throw "Unexpected frame cadence or count for $($case.Name): $($stream | ConvertTo-Json -Compress)"
        }

        & $ffmpeg.Source -v error -xerror -i $video -f null NUL
        if ($LASTEXITCODE -ne 0) { throw "Full MP4 decode failed for $($case.Name)." }

        $occludedFrame = Join-Path $testRoot ($case.Name + '-occluded.png')
        $restoredFrame = Join-Path $testRoot ($case.Name + '-restored.png')
        & $ffmpeg.Source -v error -ss 1.2 -i $video -frames:v 1 -update 1 -y $occludedFrame
        if ($LASTEXITCODE -ne 0) { throw "Could not extract the occlusion-check frame for $($case.Name)." }
        & $ffmpeg.Source -v error -ss 5.0 -i $video -frames:v 1 -update 1 -y $restoredFrame
        if ($LASTEXITCODE -ne 0) { throw "Could not extract the restore-check frame for $($case.Name)." }

        Add-Type -AssemblyName System.Drawing
        $before = [System.Drawing.Bitmap]::FromFile($occludedFrame)
        $after = [System.Drawing.Bitmap]::FromFile($restoredFrame)
        try {
            $a = $before.GetPixel([int]($before.Width / 2), [int]($before.Height / 2))
            $b = $after.GetPixel([int]($after.Width / 2), [int]($after.Height / 2))
            if ($a.R -gt 180 -and $a.G -lt 80 -and $a.B -gt 180) {
                throw "The unrelated magenta occluder appeared in the $($case.Name) recording."
            }
            $pixelChange = [Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B)
            if ($pixelChange -lt 12) { throw "The $($case.Name) frame did not change after target restore." }
        }
        finally {
            $before.Dispose()
            $after.Dispose()
        }

        $results += [pscustomobject]@{
            Test = $case.Name
            Backend = 'Windows Graphics Capture'
            SizeBytes = [int64]$probe.format.size
            Encoded = "$($stream.width)x$($stream.height)"
            FPS = $stream.avg_frame_rate
            Frames = [int64]$stream.nb_frames
            DurationSeconds = [double]$stream.duration
            FullDecode = 'ok'
            OcclusionAndRestore = 'ok'
        }
    }

    $closedVideo = Join-Path $testRoot 'closed-window.mp4'
    $closeOutput = & $windowHarness $closedVideo 671 377 2 close 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Closing the selected window failed: $($closeOutput -join ' ')" }
    if (-not ($closeOutput -join ' ').Contains('source_close=expected; output_finalized=true')) {
        throw "Window close was not reported and finalized: $($closeOutput -join ' ')"
    }
    $closedJson = & $ffprobe.Source -v error -select_streams v:0 -show_entries 'stream=codec_name,width,height,avg_frame_rate,nb_frames,duration:format=size' -of json $closedVideo
    if ($LASTEXITCODE -ne 0) { throw 'ffprobe failed for the closed-window MP4.' }
    $closedProbe = ($closedJson -join [Environment]::NewLine) | ConvertFrom-Json
    $closedStream = $closedProbe.streams | Select-Object -First 1
    if ($closedStream.codec_name -ne 'h264' -or $closedStream.avg_frame_rate -ne '30/1' -or [int64]$closedStream.nb_frames -lt 30) {
        throw "Unexpected closed-window stream metadata: $($closedStream | ConvertTo-Json -Compress)"
    }
    & $ffmpeg.Source -v error -xerror -i $closedVideo -f null NUL
    if ($LASTEXITCODE -ne 0) { throw 'The closed-window MP4 did not fully decode.' }
    $results += [pscustomobject]@{
        Test = 'closed-window'
        Backend = 'Windows Graphics Capture'
        SizeBytes = [int64]$closedProbe.format.size
        Encoded = "$($closedStream.width)x$($closedStream.height)"
        FPS = $closedStream.avg_frame_rate
        Frames = [int64]$closedStream.nb_frames
        DurationSeconds = [double]$closedStream.duration
        FullDecode = 'ok'
        OcclusionAndRestore = 'not-applicable'
    }

    $longVideo = Join-Path $testRoot 'over-one-hour.mp4'
    $longOutput = & $longHarness $longVideo 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Long-duration writer failed: $($longOutput -join ' ')" }
    $longJson = & $ffprobe.Source -v error -select_streams v:0 -show_entries 'stream=codec_name,width,height,avg_frame_rate,nb_frames,duration:format=size' -of json $longVideo
    if ($LASTEXITCODE -ne 0) { throw 'ffprobe failed for the long-duration MP4.' }
    $longProbe = ($longJson -join [Environment]::NewLine) | ConvertFrom-Json
    $longStream = $longProbe.streams | Select-Object -First 1
    if ($longStream.avg_frame_rate -ne '30/1' -or [int64]$longStream.nb_frames -ne 108030 -or [double]$longStream.duration -lt 3601) {
        throw "Unexpected long-duration stream metadata: $($longStream | ConvertTo-Json -Compress)"
    }
    & $ffmpeg.Source -v error -xerror -i $longVideo -f null NUL
    if ($LASTEXITCODE -ne 0) { throw 'Full decode failed for the long-duration MP4.' }
    $results | Format-Table -AutoSize
    [pscustomobject]@{
        Test = 'writer-over-one-hour'
        SizeBytes = [int64]$longProbe.format.size
        Encoded = "$($longStream.width)x$($longStream.height)"
        FPS = $longStream.avg_frame_rate
        Frames = [int64]$longStream.nb_frames
        DurationSeconds = [double]$longStream.duration
        FullDecode = 'ok'
    } | Format-List
    $success = $true
}
finally {
    if ($success -and -not $KeepArtifacts) {
        $tempPrefix = [System.IO.Path]::GetFullPath((Join-Path $env:TEMP 'RScreenRecSmoke-'))
        if (-not $resolvedTestRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a test path outside the dedicated temporary directory: $resolvedTestRoot"
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $resolvedTestRoot) {
        Write-Host "Smoke-test artifacts retained at: $resolvedTestRoot"
    }
}

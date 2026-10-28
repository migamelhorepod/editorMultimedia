[CmdletBinding()]
param(
    [ValidateRange(1,10240)]
    [int]$MediaMiB = 64,
    [string]$DriveRoot = "D:\",
    [string]$OutputDirectory = "TestResults/physical-windows11-hdd",
    [switch]$IncludeCrashRecovery
)
$ErrorActionPreference = "Stop"
# Local, explicit physical homologation. Do not let a CI Windows runner
# masquerade as the owner's Windows 11 machine and conventional D: HDD.
if ($env:GITHUB_ACTIONS -eq "true") {
    throw "Physical D: homologation must run on the user's own Windows 11 computer, not GitHub Actions."
}
if (-not $IsWindows) { throw "Windows 11 is required." }
$os = Get-CimInstance Win32_OperatingSystem
if ($os.Caption -notmatch "Windows 11") { throw "Target OS must be Windows 11." }
$drive = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($DriveRoot))
if ($drive -ne "D:\") { throw "Only the explicitly approved D: volume can be used." }
if (-not (Test-Path -LiteralPath $drive)) { throw "Physical D: volume unavailable." }
$disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='D:'"
if (-not $disk -or $disk.DriveType -ne 3) { throw "D: must be a locally mounted fixed disk." }
$required = [long]$MediaMiB * 1MB
if ([long]$disk.FreeSpace -lt ($required * 2 + 1GB)) {
    throw "D: has insufficient free space for isolated fixture and testing."
}
if (-not (Test-Path -LiteralPath "tests/Editor.Timeline.Tests/Editor.Timeline.Tests.csproj")) {
    throw "Execute from repository root, in the Visual Studio checkout."
}
$case = Join-Path $drive ("editor-poc0032-physical-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $case -Force | Out-Null
$oldTemp = $env:TEMP
$oldTmp = $env:TMP
$runStart = [DateTime]::UtcNow
$report = [ordered]@{
    schema = "poc0032-windows11-hdd-physical-v1"
    machineLocalExecution = $true
    executedUtc = $runStart.ToString("o")
    os = $os.Caption
    volume = $drive
    mediaMiB = $MediaMiB
    diskModel = @()
    diskTypeReported = "unverified"
    freeBytesBefore = [long]$disk.FreeSpace
    testsExitCode = -1
    testsPassed = $false
    sourceHashUnchanged = $false
    noStagingFiles = $false
    cleanupSucceeded = $false
    readyForProduction = $false
    readyForDistribution = $false
    note = "Only tests run on actual D:; production remains blocked by native CVEs, license and full crash/10GB gates."
}
try {
    try {
        $report.diskModel = @((Get-CimInstance Win32_DiskDrive | Select-Object -ExpandProperty Model))
    } catch {
        $report.diskModel = @("Unable to query disk model")
    }
    $mediaPath = Join-Path $case "real-hdd-fixture.wav"
    # Create actual bytes, never sparse SetLength, to measure conventional HD writes.
    # WAV 8-bit mono, 8kHz; large files must not overflow RIFF uint32.
    if ($MediaMiB -gt 4095) {
        throw "WAV RIFF cannot encode >4GiB. Use a separately approved MP4/large-file fixture for 10GiB."
    }
    $bodyBytes = [int64]$MediaMiB * 1MB
    $stream = [IO.File]::Open($mediaPath, [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $w = [IO.BinaryWriter]::new($stream)
        try {
            $w.Write([Text.Encoding]::ASCII.GetBytes("RIFF"))
            $w.Write([int]($bodyBytes + 36))
            $w.Write([Text.Encoding]::ASCII.GetBytes("WAVEfmt "))
            $w.Write([int]16); $w.Write([short]1); $w.Write([short]1)
            $w.Write([int]8000); $w.Write([int]8000)
            $w.Write([short]1); $w.Write([short]8)
            $w.Write([Text.Encoding]::ASCII.GetBytes("data"))
            $w.Write([int]$bodyBytes)
            $buffer = [byte[]]::new(1MB)
            [Array]::Fill($buffer, [byte]128)
            for ($remaining = $bodyBytes; $remaining -gt 0; ) {
                $size = [int][Math]::Min($remaining, $buffer.Length)
                $w.Write($buffer, 0, $size)
                $remaining -= $size
            }
        } finally { $w.Dispose() }
    } finally { $stream.Dispose() }
    $baseline = (Get-FileHash -LiteralPath $mediaPath -Algorithm SHA256).Hash
    $env:TEMP = $case
    $env:TMP = $case
    # Integration tests each create their own uniquely named subfolder in D:
    # the packaged sandbox still has to have been built and ACL-prepared locally.
    $filter = "FullyQualifiedName~AppContainerSeekableFdTests|FullyQualifiedName~WpfPackagedWorkerTests|FullyQualifiedName~AppContainerRealMetadataTests"
    if ($IncludeCrashRecovery) {
        $filter += "|FullyQualifiedName~AppContainerStageRecoveryTests"
    }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & dotnet test tests/Editor.Timeline.Tests/Editor.Timeline.Tests.csproj --configuration Release --filter $filter --logger "trx;LogFileName=physical.trx" --results-directory $case
    $report.testsExitCode = $LASTEXITCODE
    $timer.Stop()
    $report.testDurationSeconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
    $report.testsPassed = ($report.testsExitCode -eq 0)
    $report.sourceHashUnchanged = ($baseline -eq (Get-FileHash -LiteralPath $mediaPath -Algorithm SHA256).Hash)
    $report.noStagingFiles = (@(Get-ChildItem $case -Filter ".editorworker-*" -File -Recurse).Count -eq 0)
}
finally {
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
    $report.freeBytesAfter = [long](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='D:'").FreeSpace
    $report.readyForProduction = $false
    $report.readyForDistribution = $false
    # Keep evidence and synthetic files until operator reviews, no sweeping
    # directory delete on the user's physical D: volume.
    $report.fixtureDirectory = $case
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory "physical-hdd-evidence.json") -Encoding utf8
    Write-Host "Evidence saved. Fixture remains in $case for review."
}
if (-not $report.testsPassed -or -not $report.sourceHashUnchanged -or -not $report.noStagingFiles) {
    throw "Physical test failed or was incomplete. Inspect evidence and TRX."
}
Write-Host "Physical synthetic-media checks passed; 10 GiB and production release are NOT homologated."

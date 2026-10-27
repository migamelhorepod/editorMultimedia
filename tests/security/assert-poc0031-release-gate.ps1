param(
    [string]$AuditPath = "TestResults/sec-006-wfp-audit.json",
    [string]$OutputPath = "TestResults/poc0031-gate.json",
    [string]$ResultsRoot = "TestResults"
)
$ErrorActionPreference = "Stop"
$names = @()
foreach ($file in @(Get-ChildItem $ResultsRoot -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue)) {
    [xml]$xml = Get-Content $file.FullName -Raw
    foreach ($test in @($xml.TestRun.Results.UnitTestResult)) {
        if ($test.outcome -eq "Passed") { $names += [string]$test.testName }
    }
}
function HasProof([string]$fragment, [int]$minimum = 1) {
    @($names | Where-Object { $_.Contains($fragment) }).Count -ge $minimum
}
$wfp = $false
$wfpReason = "No correlated WFP 5157 evidence."
if (Test-Path $AuditPath) {
    $audit = Get-Content $AuditPath -Raw | ConvertFrom-Json
    $wfp = ($audit.policyProvenForWorker -eq $true -and
        @($audit.correlatedDeniedEvents).Count -gt 0 -and
        [int]$audit.workerProcessId -gt 0)
    $wfpReason = [string]$audit.reason
}
$checks = [ordered]@{
    "SEC-006-correlated-WFP-denial" = $wfp
    "IPC-distinct-AppContainer-SID-denied" = (HasProof "ForeignPackageSidIsDeniedByWindowsNotByTimeout")
    "IPC-real-worker-handshake" = (HasProof "RestrictedWorkerHandshake")
    "FFprobe-under-AppContainer-allowed-media" = (HasProof "RealCodecCanReadOnlyExplicitSyntheticWavInAppContainer" 2)
    "OS-denial-private-and-path-escapes" = ((HasProof "RealWorkerCannotReadPrivateFileInsideWindowsAppContainer") -and
        (HasProof "SymbolicLinkCannotEscapeToPrivateUnsharedFile") -and
        (HasProof "ParentEscapeAndLinkToAnotherLibraryCannotBeRegistered") -and
        (HasProof "ValidGrantOnlyMapsItsOwnActiveLibraryMedia"))
}
$report = [ordered]@{
    schema = "poc0031-security-release-gate-v2"
    evaluatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    readyForMain = (@($checks.Values | Where-Object { -not $_ }).Count -eq 0)
    checks = $checks
    wfpReason = $wfpReason
    passedUnitResultCount = $names.Count
    caveat = "Unit tests prove synthetic media only; production FFmpeg/ffprobe path activation remains gated."
}
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputPath -Encoding UTF8
if (-not $report.readyForMain) {
    Write-Error "POC-003.1 evidence gate blocked. Inspect poc0031-gate.json."
    exit 1
}

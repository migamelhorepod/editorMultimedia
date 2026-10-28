param(
    [string]$ResultsRoot = "TestResults",
    [string]$AuditPath = "TestResults/security-audit/dependency-audit.json",
    [string]$OutputPath = "TestResults/poc0032-fd-research-gate.json"
)
$ErrorActionPreference = "Stop"
$requiredTests = @(
    "GivenAuthorizedWavWithoutSourceAclChange_WhenFdProbeRuns_ThenItReadsMetadataWithoutStaging",
    "GivenMp4WithMoovAfterMdat_WhenFdProbeSeeks_ThenItReadsMetadataWithoutStaging",
    "GivenAuthorizedWav_WhenFfmpegDecodesFdInSandbox_ThenNoStageAndSourceIsUnchanged",
    "GivenRevokedGrant_WhenFdProbeIsRequested_ThenNoWorkerGetsAnInputHandle",
    "GivenRegisteredMediaWasReplacedByAnotherFileAtSamePath_WhenGrantedHandleIsRequested_ThenItFailsClosed"
)
$results = @()
foreach ($trx in @(Get-ChildItem $ResultsRoot -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue)) {
    [xml]$xml = Get-Content $trx.FullName -Raw
    foreach ($test in @($xml.TestRun.Results.UnitTestResult)) {
        $results += [pscustomobject]@{
            name = [string]$test.testName
            outcome = [string]$test.outcome
        }
    }
}
$checks = [ordered]@{}
foreach ($name in $requiredTests) {
    $matches = @($results | Where-Object { $_.name.Contains($name) })
    $checks[$name] = ($matches.Count -eq 1 -and $matches[0].outcome -eq "Passed")
}
$auditOk = $false
$gpl = $null
if (Test-Path $AuditPath) {
    $audit = Get-Content $AuditPath -Raw | ConvertFrom-Json
    $auditOk = ($audit.auditSucceeded -eq $true -and
        @($audit.highOrCriticalVulnerabilities).Count -eq 0)
    $gpl = $audit.ffmpeg.gplBuild
}
$checks["Dependency-audit-completed-without-high-or-critical-findings"] = $auditOk
$report = [ordered]@{
    schema = "poc0032-seekable-fd-research-v1"
    evaluatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    researchGatePassed = (@($checks.Values | Where-Object { $_ -ne $true }).Count -eq 0)
    readyForProduction = $false
    readyForDistribution = $false
    ffmpegGplBuild = $gpl
    checks = $checks
    caveat = "Research gate only. Production AppContainer flows, recovery, 10GB HDD and GPL distribution have not been homologated."
}
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
$report | ConvertTo-Json -Depth 10 | Set-Content -Path $OutputPath -Encoding UTF8
if (-not $report.researchGatePassed) {
    Write-Error "Seekable AppContainer research proof failed; production remains blocked."
    exit 1
}
Write-Host "Windows seekable FD research gate passed. Production and distribution remain blocked."

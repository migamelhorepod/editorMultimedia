param(
    [string]$AuditPath = "TestResults/sec-006-wfp-audit.json",
    [string]$OutputPath = "TestResults/poc0031-gate.json"
)
$ErrorActionPreference = "Stop"
$checks = [ordered]@{
    "SEC-006-correlated-WFP-denial" = $false
    "IPC-distinct-AppContainer-SID-denied" = $false
    "FFprobe-under-AppContainer-allowed-media" = $false
    "FFmpeg-under-AppContainer-allowed-media" = $false
    "OS-denial-inactive-library-and-path-escapes" = $false
}
$details = [ordered]@{}
if (Test-Path $AuditPath) {
    $audit = Get-Content $AuditPath -Raw | ConvertFrom-Json
    $checks["SEC-006-correlated-WFP-denial"] = ($audit.policyProvenForWorker -eq $true)
    $details.wfp = $audit.reason
} else { $details.wfp = "Audit file missing." }
$report = [ordered]@{
    schema = "poc0031-security-release-gate-v1"
    evaluatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    readyForMain = (@($checks.Values | Where-Object { -not $_ }).Count -eq 0)
    checks = $checks
    details = $details
}
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputPath -Encoding UTF8
if (-not $report.readyForMain) {
    Write-Error "POC-003.1 is NOT ready for main: remaining security evidence is missing."
    exit 1
}

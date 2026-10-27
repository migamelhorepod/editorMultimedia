param(
    [string] $OutputPath = "TestResults/sec-006-wfp-audit.json"
)
$ErrorActionPreference = "Stop"
# Read-only collection. Do not modify machine-wide audit policy or firewall.
$policy = (& auditpol.exe /get /subcategory:"Filtering Platform Connection" 2>&1 | Out-String)
$policyExit = $LASTEXITCODE
$events = @()
$eventError = $null
try {
    $start = (Get-Date).AddMinutes(-15)
    $events = @(Get-WinEvent -FilterHashtable @{LogName="Security"; Id=5157,5156; StartTime=$start} -ErrorAction Stop |
        Select-Object -First 100 TimeCreated, Id, ProviderName, Message)
} catch {
    $eventError = $_.Exception.Message
}
$report = [ordered]@{
    schema = "sec-006-wfp-audit-v1"
    collectedUtc = (Get-Date).ToUniversalTime().ToString("o")
    auditpolExitCode = $policyExit
    auditpolText = $policy
    eventCount = @($events).Count
    eventReadError = $eventError
    events = $events
    # A 5157 event unrelated to the worker is NOT evidence of its isolation.
    policyProvenForWorker = $false
    reason = "Requires correlated 5157 denied-connection event (process, destination, time and filter)."
}
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$report | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "WFP audit collected; verification remains inconclusive absent correlated 5157."

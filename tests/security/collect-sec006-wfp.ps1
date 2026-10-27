param(
    [string] $OutputPath = "TestResults/sec-006-wfp-audit.json",
    [string] $WorkerPath = "",
    [int] $WorkerProcessId = 0,
    [string] $DestinationAddress = "1.1.1.1",
    [int] $DestinationPort = 443,
    [string] $ProbeStartUtc = "",
    [string] $ProbeEndUtc = ""
)
$ErrorActionPreference = "Stop"
# Read-only: requires prior WFP audit configuration; never enable auditpol in CI.
$policy = (& auditpol.exe /get /subcategory:"Filtering Platform Connection" 2>&1 | Out-String)
$policyExit = $LASTEXITCODE
$events = @()
$errorText = $null
$matches = @()
$samePid = 0
$sameDestination = 0
$sameApplication = 0
$blockedEventCount = 0
$start = if ($ProbeStartUtc) { ([datetime]::Parse($ProbeStartUtc)).ToUniversalTime().AddSeconds(-3) } else { (Get-Date).ToUniversalTime().AddMinutes(-15) }
$end = if ($ProbeEndUtc) { ([datetime]::Parse($ProbeEndUtc)).ToUniversalTime().AddSeconds(3) } else { (Get-Date).ToUniversalTime() }
try {
    $events = @(Get-WinEvent -FilterHashtable @{LogName="Security"; Id=5157,5156; StartTime=$start; EndTime=$end} -ErrorAction Stop)
    foreach ($event in $events) {
        [xml]$xml = $event.ToXml()
        $fields = @{}
        foreach ($field in $xml.Event.EventData.Data) { $fields[[string]$field.Name] = [string]$field.'#text' }
        $pathValue = $fields['Application']
        if (-not $pathValue) { $pathValue = $fields['ProcessName'] }
        $destination = $fields['DestAddress']
        $port = $fields['DestPort']
        if (-not $destination) { $destination = $fields['DestinationAddress'] }
        if (-not $port) { $port = $fields['DestinationPort'] }
        $workerMatches = $WorkerPath -and $pathValue -and ([string]::Equals($pathValue, $WorkerPath, [StringComparison]::OrdinalIgnoreCase))
        if ($event.Id -eq 5157) {
            $blockedEventCount++
            if ($fields['ProcessID'] -eq "$WorkerProcessId") { $samePid++ }
            if ($destination -eq $DestinationAddress -and $port -eq "$DestinationPort") { $sameDestination++ }
            if ($workerMatches) { $sameApplication++ }
        }
        if ($event.Id -eq 5157 -and $workerMatches -and $WorkerProcessId -gt 0 -and
            $fields['ProcessID'] -eq "$WorkerProcessId" -and
            $destination -eq $DestinationAddress -and $port -eq "$DestinationPort") {
            $matches += [ordered]@{eventId=$event.Id;timeUtc=$event.TimeCreated.ToUniversalTime().ToString('o');application=$pathValue;destination=$destination;port=$port;processId=$fields['ProcessID'];filterRunTimeId=$fields['FilterRTID']}
        }
    }
} catch {
    $errorText = $_.Exception.Message
}
$report = [ordered]@{
    schema = "sec-006-wfp-audit-v2"
    collectedUtc = (Get-Date).ToUniversalTime().ToString("o")
    auditpolExitCode = $policyExit
    auditpolText = $policy
    probeStartUtc = $ProbeStartUtc
    probeEndUtc = $ProbeEndUtc
    destinationAddress = $DestinationAddress
    destinationPort = $DestinationPort
    workerPath = $WorkerPath
    workerProcessId = $WorkerProcessId
    eventCount = @($events).Count
    blockedEventCount = $blockedEventCount
    samePidDeniedCount = $samePid
    sameDestinationDeniedCount = $sameDestination
    sameApplicationDeniedCount = $sameApplication
    eventReadError = $errorText
    correlatedDeniedEvents = @($matches)
    policyProvenForWorker = (@($matches).Count -gt 0)
    reason = if (@($matches).Count -gt 0) {"Correlated WFP 5157 denial for worker, destination and time window."} else {"No correlated WFP 5157 denial; timeout is inconclusive."}
}
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$report | ConvertTo-Json -Depth 7 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host $report.reason
Write-Host "WFP diagnostics: total $($report.eventCount), 5157 $blockedEventCount, PID $samePid, destination $sameDestination, application $sameApplication."

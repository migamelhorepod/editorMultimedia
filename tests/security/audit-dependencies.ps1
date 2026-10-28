param(
    [string]$Solution = "EditorMultimidia.sln",
    [string]$NodeDirectory = "tests/playwright",
    [string]$CodecDirectory = "TestResults/sandbox-worker",
    [string]$OutputDirectory = "TestResults/security-audit"
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$report = [ordered]@{
    schema = "editor-dependency-audit-v1"
    evaluatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    auditSucceeded = $false
    highOrCriticalVulnerabilities = @()
    allVulnerabilities = @()
    npmLicenses = @()
    nugetPackages = @()
    ffmpeg = @{}
    manualLicenseReviewRequired = $true
    errors = @()
}
function Save-Report {
    $report | ConvertTo-Json -Depth 35 |
        Set-Content -LiteralPath (Join-Path $OutputDirectory "dependency-audit.json") -Encoding utf8
}
try {
    $nugetRaw = & dotnet list $Solution package --vulnerable --include-transitive --format json 2>&1
    $nugetExit = $LASTEXITCODE
    ($nugetRaw -join [Environment]::NewLine) | Set-Content -LiteralPath (Join-Path $OutputDirectory "nuget-vulnerabilities.json") -Encoding utf8
    if ($nugetExit -ne 0) { throw "NuGet vulnerability query failed: exit $nugetExit." }
    $nuget = ($nugetRaw -join [Environment]::NewLine) | ConvertFrom-Json
    foreach ($project in @($nuget.projects)) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($kind in @("topLevelPackages", "transitivePackages")) {
                foreach ($p in @($framework.$kind)) {
                    foreach ($v in @($p.vulnerabilities)) {
                        if ($null -eq $v) { continue }
                        $severity = [string]$v.severity
                        $entry = [ordered]@{
                            ecosystem = "NuGet"; project = [string]$project.path
                            name = [string]$p.id; version = [string]$p.resolvedVersion
                            severity = $severity; advisory = [string]$v.advisoryurl
                        }
                        $report.allVulnerabilities += $entry
                        if ($severity -match "^(high|critical|3|4)$") {
                            $report.highOrCriticalVulnerabilities += $entry
                        }
                    }
                }
            }
        }
    }

    $nugetAllRaw = & dotnet list $Solution package --include-transitive --format json 2>&1
    if ($LASTEXITCODE -ne 0) { throw "NuGet inventory command failed." }
    $nugetAll = ($nugetAllRaw -join [Environment]::NewLine) | ConvertFrom-Json
    $seen = @{}
    foreach ($project in @($nugetAll.projects)) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($kind in @("topLevelPackages", "transitivePackages")) {
                foreach ($p in @($framework.$kind)) {
                    if (-not $p.id -or -not $p.resolvedVersion) { continue }
                    $key = "$($p.id)@$($p.resolvedVersion)"
                    if ($seen.ContainsKey($key)) { continue }
                    $seen[$key] = $true
                    $packageFolder = Join-Path (Join-Path $env:USERPROFILE ".nuget/packages") ([string]$p.id).ToLowerInvariant()
                    $packageFolder = Join-Path $packageFolder ([string]$p.resolvedVersion).ToLowerInvariant()
                    $manifest = Get-ChildItem $packageFolder -Filter "*.nuspec" -File -ErrorAction SilentlyContinue | Select-Object -First 1
                    $license = "UNKNOWN"
                    if ($manifest) {
                        [xml]$xml = Get-Content -LiteralPath $manifest.FullName -Raw
                        $licenseNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
                        $legacyNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='licenseUrl']")
                        if ($licenseNode) { $license = "$($licenseNode.GetAttribute('type')):$($licenseNode.InnerText)" }
                        elseif ($legacyNode) { $license = "legacy-url:$($legacyNode.InnerText)" }
                    }
                    $report.nugetPackages += [ordered]@{
                        name = [string]$p.id; version = [string]$p.resolvedVersion
                        license = $license
                    }
                }
            }
        }
    }

    Push-Location $NodeDirectory
    try {
        $npmRaw = & npm audit --json 2>&1
        $npmExit = $LASTEXITCODE
    } finally { Pop-Location }
    $npmContent = ($npmRaw -join [Environment]::NewLine)
    $npmContent | Set-Content -LiteralPath (Join-Path $OutputDirectory "npm-vulnerabilities.json") -Encoding utf8
    $npm = $npmContent | ConvertFrom-Json
    if ($npm.error -or $npmExit -gt 1) {
        throw "npm audit failed to obtain reliable registry advisories (exit $npmExit)."
    }
    foreach ($property in $npm.vulnerabilities.PSObject.Properties) {
        $p = $property.Value
        $entry = [ordered]@{
            ecosystem = "npm"; name = $property.Name; severity = [string]$p.severity
            range = [string]$p.range; advisory = (@($p.via | Where-Object { $_ -isnot [string] } |
                ForEach-Object { $_.url }) -join ",")
        }
        $report.allVulnerabilities += $entry
        if ($p.severity -in @("high", "critical")) {
            $report.highOrCriticalVulnerabilities += $entry
        }
    }
    $lock = Get-Content -LiteralPath (Join-Path $NodeDirectory "package-lock.json") -Raw | ConvertFrom-Json -AsHashtable
    # npm lockfiles intentionally have an empty-string root package key.
    # PowerShell's default PSCustomObject conversion rejects it.
    foreach ($property in $lock["packages"].GetEnumerator()) {
        if ($property.Key -eq "") { continue }
        $p = $property.Value
        $report.npmLicenses += [ordered]@{
            name = $property.Key; version = [string]$p.version
            license = $(if ($p.license) { [string]$p.license } else { "UNKNOWN" })
            integrity = [string]$p.integrity
        }
    }
    $ffmpeg = Join-Path $CodecDirectory "ffmpeg.exe"
    $ffprobe = Join-Path $CodecDirectory "ffprobe.exe"
    foreach ($path in @($ffmpeg, $ffprobe)) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Codec executable missing: $path" }
    }
    $versionLines = @(& $ffmpeg -version)
    if ($LASTEXITCODE -ne 0) { throw "Cannot read FFmpeg version/configuration." }
    $configuration = @($versionLines | Where-Object { $_ -like "configuration:*" }) -join " "
    $report.ffmpeg = [ordered]@{
        version = [string]$versionLines[0]; configuration = $configuration
        ffmpegSha256 = (Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash
        ffprobeSha256 = (Get-FileHash -LiteralPath $ffprobe -Algorithm SHA256).Hash
        gplBuild = ($configuration -match "(^|\s)--enable-gpl(\s|$)")
        licenseReview = "Inspect real distribution build flags and LGPL/GPL obligations before release."
    }
    # A lightweight CycloneDX 1.5 component inventory; license declarations
    # are evidence to review, not an automatic conclusion of legal compliance.
    $components = @()
    foreach ($p in $report.npmLicenses) {
        $components += [ordered]@{
            type = "library"; name = [string]$p.name; version = [string]$p.version
            "bom-ref" = "npm:$($p.name)@$($p.version)"
            licenses = @(@{ license = @{ name = [string]$p.license } })
            properties = @(@{ name = "evidence"; value = "npm-package-lock" })
        }
    }
    foreach ($p in $report.nugetPackages) {
        $components += [ordered]@{
            type = "library"; name = [string]$p.name; version = [string]$p.version
            "bom-ref" = "nuget:$($p.name)@$($p.version)"
            licenses = @(@{ license = @{ name = [string]$p.license } })
            properties = @(@{ name = "evidence"; value = "nuget-installed-nuspec" })
        }
    }
    $components += [ordered]@{
        type = "application"; name = "ffmpeg"; version = [string]$report.ffmpeg.version
        "bom-ref" = "ffmpeg-bundle"
        hashes = @(@{ alg = "SHA-256"; content = [string]$report.ffmpeg.ffmpegSha256 })
        properties = @(@{ name = "license-review"; value = "required" })
    }
    $bom = [ordered]@{
        bomFormat = "CycloneDX"; specVersion = "1.5"; version = 1
        metadata = @{ timestamp = $report.evaluatedUtc }
        components = $components
    }
    $bom | ConvertTo-Json -Depth 35 |
        Set-Content -LiteralPath (Join-Path $OutputDirectory "sbom.cdx.json") -Encoding utf8
    $unknown = @($report.npmLicenses | Where-Object { $_.license -eq "UNKNOWN" }).Count
    $report.auditSucceeded = $true
    Write-Host "NuGet/NPM advisories gathered; total findings: $($report.allVulnerabilities.Count)"
    Write-Host "High/critical: $($report.highOrCriticalVulnerabilities.Count); npm licenses unknown: $unknown"
    Write-Host "FFmpeg SHA256: $($report.ffmpeg.ffmpegSha256)"
}
catch {
    $report.errors += [string]$_
    Write-Error "Dependency audit could not be completed: $_" -ErrorAction Continue
}
finally {
    Save-Report
}
if (-not $report.auditSucceeded) { exit 2 }
if ($report.highOrCriticalVulnerabilities.Count -gt 0) {
    Write-Error "High/critical dependency vulnerabilities detected. Inspect dependency-audit.json." -ErrorAction Continue
    exit 1
}
exit 0

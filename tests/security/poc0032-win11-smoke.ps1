param([string]$LibraryRoot = "D:\")
$ErrorActionPreference = "Stop"
$os = Get-CimInstance Win32_OperatingSystem
if ($os.Caption -notmatch "Windows 11") { throw "Windows 11 required: $($os.Caption)" }
if (-not (Test-Path -LiteralPath $LibraryRoot -PathType Container)) { throw "Required library drive unavailable" }
dotnet test tests/Editor.Timeline.Tests/Editor.Timeline.Tests.csproj --configuration Release --filter "FullyQualifiedName~Poc0032LifecycleTests"
if ($LASTEXITCODE -ne 0) { throw "POC-003.2 tests failed" }
Write-Host "Domain tests passed. Live WPF busy modal, physical disk disconnect and force-quit recovery remain manual acceptance items."

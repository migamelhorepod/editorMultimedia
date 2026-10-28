param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)
$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") { throw "Este empacotamento requer Windows." }

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\Editor.Timeline.Wpf\Editor.Timeline.Wpf.csproj"
& dotnet build $project --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "Não foi possível compilar o editor e o worker." }

$bundle = Join-Path $repo "src\Editor.Timeline.Wpf\bin\$Configuration\net10.0-windows\worker"
$workerProject = Join-Path $repo "src\Editor.Security.Worker\Editor.Security.Worker.csproj"
# AppContainer cannot assume access to the global .NET installation. Publish the
# isolated worker WITH its own stable .NET 10 runtime, like the CI security proof.
& dotnet publish $workerProject --configuration $Configuration --runtime win-x64 --self-contained true -p:PublishSingleFile=false --output $bundle
if ($LASTEXITCODE -ne 0) { throw "Falha na publicação self-contained do worker." }
$worker = Join-Path $bundle "Editor.Security.Worker.exe"
if (-not (Test-Path $worker)) { throw "O publish não produziu o executável AppContainer." }

# Prefer the actual paired binaries, not Chocolatey's PATH shims.
# A shim can execute successfully during packaging but fail inside AppContainer
# because its external Chocolatey installation target is inaccessible.
$pair = $null
$chocoTools = Join-Path $env:ProgramData "chocolatey\lib\ffmpeg\tools"
if (Test-Path $chocoTools) {
    $pair = Get-ChildItem $chocoTools -Filter ffprobe.exe -File -Recurse |
        Where-Object { Test-Path (Join-Path $_.DirectoryName "ffmpeg.exe") } |
        Select-Object -First 1
}
if ($pair) {
    $codecDir = $pair.DirectoryName
} else {
    $ffmpeg = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    $ffprobe = Get-Command ffprobe.exe -ErrorAction SilentlyContinue
    if (-not $ffmpeg -or -not $ffprobe) {
        throw "Instale FFmpeg e ffprobe estáveis 9.x antes de preparar o worker."
    }
    if ($ffmpeg.Source -match '[\\/]chocolatey[\\/]bin[\\/]' -or
        $ffprobe.Source -match '[\\/]chocolatey[\\/]bin[\\/]') {
        throw "Não copie executáveis intermediários do Chocolatey. Localize os binários reais em uma mesma pasta."
    }
    $codecDir = Split-Path -Parent $ffprobe.Source
    if (-not (Test-Path (Join-Path $codecDir "ffmpeg.exe"))) {
        throw "FFmpeg e ffprobe reais devem estar juntos na mesma pasta."
    }
}

foreach ($name in @("ffmpeg", "ffprobe")) {
    $tool = Join-Path $codecDir ($name + ".exe")
    $banner = & $tool -version | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or $banner -notmatch 'version\s+9\.[0-9]+') {
        throw "$name deve ser versão estável 9.x: $banner"
    }
    Copy-Item $tool (Join-Path $bundle ($name + ".exe")) -Force
    if ((Get-Item (Join-Path $bundle ($name + ".exe"))).Length -ne (Get-Item $tool).Length) {
        throw "Falha ao copiar o binário real de $name."
    }
}
Write-Host "Ferramentas reais copiadas do mesmo diretório: $codecDir"
# Windows must authorize the exact AppContainer package identity to read and
# execute this bundle. Never grant Everyone, Users or all AppContainers.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class WorkerBundleSid {
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr sid);
    public static string Get() {
        IntPtr nativeSid;
        int hr = DeriveAppContainerSidFromAppContainerName(
            "MigaMelhore.EditorMultimedia.MediaWorker", out nativeSid);
        if (hr < 0 || nativeSid == IntPtr.Zero) Marshal.ThrowExceptionForHR(hr);
        try { return new SecurityIdentifier(nativeSid).Value; }
        finally { FreeSid(nativeSid); }
    }
}
'@
$sid = [WorkerBundleSid]::Get()
& icacls.exe $bundle /grant ("*" + $sid + ":(OI)(CI)RX") /T /C
if ($LASTEXITCODE -ne 0) { throw "Falha ao configurar acesso mínimo ao bundle AppContainer." }

Write-Host "Bundle AppContainer preparado: $bundle"
Write-Host "Worker, ffmpeg e ffprobe prontos. O worker continua protegido pelo token AppContainer e Job Object."

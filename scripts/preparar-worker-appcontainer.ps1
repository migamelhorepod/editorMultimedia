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
$worker = Join-Path $bundle "Editor.Security.Worker.exe"
if (-not (Test-Path $worker)) { throw "O build não publicou o executável AppContainer." }

$ffmpeg = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
$ffprobe = Get-Command ffprobe.exe -ErrorAction SilentlyContinue
if (-not $ffmpeg -or -not $ffprobe) {
    throw "Instale os executáveis estáveis FFmpeg e ffprobe versão 9.x no PATH antes de preparar o worker. Não será usada uma versão beta nem feita instalação automática."
}

foreach ($tool in @($ffmpeg.Source, $ffprobe.Source)) {
    $name = [IO.Path]::GetFileNameWithoutExtension($tool)
    $banner = & $tool -version | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or $banner -notmatch 'version\s+9\.[0-9]+') {
        throw "$name deve ser versão estável 9.x: $banner"
    }
}

Copy-Item $ffmpeg.Source (Join-Path $bundle "ffmpeg.exe") -Force
Copy-Item $ffprobe.Source (Join-Path $bundle "ffprobe.exe") -Force
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

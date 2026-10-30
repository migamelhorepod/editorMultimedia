@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0.."

echo === POC-003.2 - Homologacao fisica D: usando somente CMD ===
echo Nenhum arquivo da sua biblioteca sera alterado.
echo Este teste NAO libera a producao nem distribui codecs.

if /I "%GITHUB_ACTIONS%"=="true" (
    echo ERRO: execute somente no seu computador Windows 11, nao no GitHub Actions.
    exit /b 1
)
if not exist "D:\" (
    echo ERRO: o volume fisico D: nao esta acessivel.
    exit /b 1
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERRO: SDK .NET 10 nao foi encontrado no CMD.
    exit /b 1
)

set "SIZE=64"
if not "%~1"=="" set "SIZE=%~1"
echo Tamanho do teste: %SIZE% MiB

echo [1/4] Restaurando pacotes...
dotnet restore EditorMultimidia.sln
if errorlevel 1 exit /b 1

echo [2/4] Compilando editor e testes em Release...
dotnet build EditorMultimidia.sln --configuration Release --no-restore
if errorlevel 1 exit /b 1

echo [3/4] Publicando worker isolado real...
dotnet publish src\Editor.Security.Worker\Editor.Security.Worker.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false --output TestResults\sandbox-worker
if errorlevel 1 exit /b 1

echo [4/4] Executando a verificacao local e emitindo evidencias...
dotnet run --project tools\Editor.PhysicalHomologation\Editor.PhysicalHomologation.csproj --configuration Release -- --size-mib %SIZE%
if errorlevel 1 (
    echo ERRO: a homologacao fisica nao passou. Consulte TestResults\physical-windows11-hdd.
    exit /b 1
)
echo TESTE LOCAL CONCLUIDO. Verifique o relatorio; producao continua bloqueada.
exit /b 0

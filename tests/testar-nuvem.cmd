@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0.."
echo === POC armazenamento na nuvem: Oracle OCI x Cloudflare R2 ===
echo Executando testes locais sem contas, credenciais, upload ou custos...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERRO: .NET 10 SDK nao encontrado.
  exit /b 1
)
dotnet test tests\Editor.CloudStorage.Tests\Editor.CloudStorage.Tests.csproj --configuration Release --logger "trx;LogFileName=cloud-offline.trx" --results-directory TestResults\cloud-poc
if errorlevel 1 (
  echo Os testes da POC de nuvem falharam.
  exit /b 1
)
echo Testes locais de contrato finalizados. Isto NAO prova conexao real com Oracle ou R2.
exit /b 0

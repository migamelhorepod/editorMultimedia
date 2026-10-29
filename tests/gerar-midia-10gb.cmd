@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0.."
echo === Gerador sintetico RF64 para testes do Editor Multimidia ===
echo Nenhum arquivo pessoal sera modificado. A pasta D:\EditorMultimidia\Testes\_originais sera criada se necessario.
echo O gerador cria arquivos NOVOS, nunca substitui nem exclui arquivos existentes.
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERRO: .NET 10 SDK nao encontrado no CMD.
  exit /b 1
)
if /I "%~1"=="pequeno" (
  echo Modo de comprovacao: 64 MiB.
  dotnet run --project tools\Editor.LargeMediaFixture\Editor.LargeMediaFixture.csproj --configuration Release -- --size-mib 64
) else (
  if not "%~1"=="" (
    echo Uso: tests\gerar-midia-10gb.cmd ^[pequeno^]
    exit /b 2
  )
  echo AVISO: este comando grava 10.000.000.000 bytes no disco D: e conserva o arquivo.
  echo Verifica se ha pelo menos 22 GB livres antes de iniciar.
  dotnet run --project tools\Editor.LargeMediaFixture\Editor.LargeMediaFixture.csproj --configuration Release -- --size-gb 10
)
if errorlevel 1 (
  echo ERRO: geracao incompleta. Consulte o relatorio na pasta de fixtures.
  exit /b 1
)
echo Arquivo criado. Para processa-lo, use uma biblioteca de testes separada no aplicativo WPF.
exit /b 0

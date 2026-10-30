@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"
if /I not "%CONFIG%"=="Debug" if /I not "%CONFIG%"=="Release" (
    echo ERRO: use scripts\preparar-worker-appcontainer.cmd Debug ou Release
    exit /b 2
)
if not exist "%~dp0preparar-worker-appcontainer.ps1" (
    echo ERRO: empacotador do worker nao encontrado no repositorio.
    exit /b 1
)
echo Preparando worker AppContainer e codecs para %CONFIG%...
powershell.exe -NoProfile -File "%~dp0preparar-worker-appcontainer.ps1" -Configuration "%CONFIG%"
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
    echo ERRO: empacotamento falhou. O worker NAO esta pronto para a WPF.
    exit /b %RESULT%
)
echo SUCESSO: worker isolado empacotado. Verifique os tres executaveis na pasta worker.
exit /b 0

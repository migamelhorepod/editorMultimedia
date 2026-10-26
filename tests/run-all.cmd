@echo off
setlocal
cd /d "%~dp0.."
dotnet restore EditorMultimidia.sln
if errorlevel 1 exit /b %errorlevel%
dotnet build EditorMultimidia.sln --configuration Release --no-restore
if errorlevel 1 exit /b %errorlevel%
dotnet test tests\Editor.Timeline.Tests\Editor.Timeline.Tests.csproj --configuration Release --no-build
if errorlevel 1 exit /b %errorlevel%
pushd tests\playwright
call npm ci
if errorlevel 1 (
  popd
  exit /b 1
)
call npm test
set "TEST_EXIT=%errorlevel%"
popd
exit /b %TEST_EXIT%

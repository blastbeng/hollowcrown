@echo off
rem test.bat - build + central server boot and /health check on port 6561
setlocal
where dotnet >nul 2>nul
if errorlevel 1 if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%PATH%;%ProgramFiles%\dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo TEST FAILED: dotnet SDK 8+ not found
  exit /b 1
)
cd /d "%~dp0.."
echo == dotnet build ==
dotnet build Hollowcrown.sln -v minimal
if errorlevel 1 ( echo TEST FAILED: dotnet build & exit /b 1 )
echo == central boot + /health ==
if exist "%TEMP%\hc_test_central.log" del "%TEMP%\hc_test_central.log"
set ASPNETCORE_URLS=http://127.0.0.1:6561
start /b "" cmd /c "dotnet run --project central > %TEMP%\hc_test_central.log 2>&1"
set HEALTH=
for /l %%i in (1,1,30) do (
  if not defined HEALTH (
    timeout /t 2 /nobreak >nul
    for /f "delims=" %%h in ('curl -sf http://127.0.0.1:6561/health 2^>nul') do set HEALTH=%%h
  )
)
if not defined HEALTH (
  echo -- central log --
  type "%TEMP%\hc_test_central.log"
  echo TEST FAILED: central /health
  exit /b 2
)
echo central /health -^> %HEALTH%
echo TEST OK
exit /b 0

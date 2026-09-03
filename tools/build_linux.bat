@echo off
rem build_linux.bat - cross-build the solution for the Linux desktop target
setlocal
where dotnet >nul 2>nul
if errorlevel 1 if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%PATH%;%ProgramFiles%\dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: dotnet SDK 8+ not found in PATH
  exit /b 1
)
cd /d "%~dp0.."
dotnet build Hollowcrown.sln -r linux-x64 --self-contained false %*
exit /b %ERRORLEVEL%

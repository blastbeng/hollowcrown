@echo off
rem build_windows.bat - build the full solution (Windows native target)
setlocal
where dotnet >nul 2>nul
if errorlevel 1 if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%PATH%;%ProgramFiles%\dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: dotnet SDK 8+ not found in PATH
  exit /b 1
)
cd /d "%~dp0.."
dotnet build Hollowcrown.sln %*
exit /b %ERRORLEVEL%

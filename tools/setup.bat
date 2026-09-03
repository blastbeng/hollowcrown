@echo off
rem setup.bat - environment sanity check + package restore
setlocal
where dotnet >nul 2>nul
if errorlevel 1 if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%PATH%;%ProgramFiles%\dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: dotnet SDK 8+ not found
  exit /b 1
)
cd /d "%~dp0.."
echo dotnet:
dotnet --version
where godot >nul 2>nul && (echo godot: & godot --version) || echo godot: not found in PATH (ok for build)
dotnet restore Hollowcrown.sln
echo SETUP OK
exit /b 0

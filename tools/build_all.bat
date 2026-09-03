@echo off
rem build_all.bat - Debug + Release builds (fast correctness gate before commit)
setlocal
where dotnet >nul 2>nul
if errorlevel 1 if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%PATH%;%ProgramFiles%\dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: dotnet SDK 8+ not found in PATH
  exit /b 1
)
cd /d "%~dp0.."
dotnet build Hollowcrown.sln || exit /b 1
dotnet build Hollowcrown.sln -c Release || exit /b 1
echo BUILD ALL OK
exit /b 0

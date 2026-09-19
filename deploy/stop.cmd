@echo off
rem ============================================================================
rem  TestPilot - Windows stopper (double-click entry from deploy\)
rem  Real logic lives in scripts/stop-all.sh. Keep this file ASCII-only:
rem  cmd.exe parses rem lines with the ANSI codepage BEFORE chcp takes effect.
rem
rem  Usage: double-click deploy\stop.cmd, or run stop.cmd [--with-db]
rem  Git Bash lookup: registry (custom drive OK) -> where git -> common paths
rem ============================================================================
setlocal
chcp 65001 >nul

rem AITEST_ROOT = repo root (parent of deploy\)
set "AITEST_ROOT=%~dp0.."
set "BASH_EXE="

rem 1) Registry: written by the Git for Windows installer (HKLM machine / HKCU user scope)
for /f "tokens=2*" %%a in ('reg query "HKLM\SOFTWARE\GitForWindows" /v InstallPath 2^>nul') do if not defined BASH_EXE if exist "%%b\bin\bash.exe" set "BASH_EXE=%%b\bin\bash.exe"
if not defined BASH_EXE for /f "tokens=2*" %%a in ('reg query "HKCU\SOFTWARE\GitForWindows" /v InstallPath 2^>nul') do if not defined BASH_EXE if exist "%%b\bin\bash.exe" set "BASH_EXE=%%b\bin\bash.exe"

rem 2) Derive from git.exe on PATH: git.exe lives in <Git>\cmd\, bash.exe in <Git>\bin\
if not defined BASH_EXE for /f "delims=" %%i in ('where git 2^>nul') do if not defined BASH_EXE if exist "%%~dpi..\bin\bash.exe" set "BASH_EXE=%%~dpi..\bin\bash.exe"

rem 3) Common install paths fallback
for %%p in (
  "%ProgramFiles%\Git\bin\bash.exe"
  "%ProgramFiles(x86)%\Git\bin\bash.exe"
  "%LOCALAPPDATA%\Programs\Git\bin\bash.exe"
  "%USERPROFILE%\scoop\apps\git\current\bin\bash.exe"
  "D:\Program Files\Git\bin\bash.exe"
) do if not defined BASH_EXE if exist %%p set "BASH_EXE=%%~p"

if not defined BASH_EXE (
  echo.
  echo [ERROR] Git Bash not found. Please install Git for Windows first,
  echo         or run manually in Git Bash:  bash scripts/stop-all.sh
  echo.
  pause
  exit /b 1
)

rem cd first so bash can use a relative script path - this avoids BOTH the
rem cygpath dependency and cmd.exe's quote-stripping on "for /f" inline
rem commands when BASH_EXE contains spaces ('D:\Program' is not recognized).
cd /d "%AITEST_ROOT%"
"%BASH_EXE%" -c "exec bash scripts/stop-all.sh %*"

echo.
pause

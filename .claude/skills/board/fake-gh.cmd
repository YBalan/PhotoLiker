@echo off
rem A stand-in for gh, for board.tests.ps1 (BOARD_GH points at this file, FAKE_GH_DIR at a folder of canned answers).
rem   "gh a b ..." answers with the file  %FAKE_GH_DIR%\a-b.out  and exit code 0, or, when  a-b.err  exists, prints that to the error stream and exits 1.
rem   "gh auth refresh" / "gh auth login" print a one-time code, then play the approval: every .err file goes, and the files in after-refresh\ replace the answers.
rem   Every call is appended to calls.log.
setlocal
>> "%FAKE_GH_DIR%\calls.log" echo %*
set "KEY=%~1-%~2"
if exist "%FAKE_GH_DIR%\%KEY%.err" (
  type "%FAKE_GH_DIR%\%KEY%.err" 1>&2
  exit /b 1
)
if "%KEY%"=="auth-refresh" call :approved
if "%KEY%"=="auth-login" call :approved
if exist "%FAKE_GH_DIR%\%KEY%.out" type "%FAKE_GH_DIR%\%KEY%.out"
exit /b 0

:approved
echo ! First copy your one-time code: TEST-CODE 1>&2
echo Open this URL to continue in your web browser: https://github.com/login/device 1>&2
del /q "%FAKE_GH_DIR%\*.err" 2>nul
if exist "%FAKE_GH_DIR%\after-refresh\*" copy /y "%FAKE_GH_DIR%\after-refresh\*" "%FAKE_GH_DIR%\" >nul
exit /b 0

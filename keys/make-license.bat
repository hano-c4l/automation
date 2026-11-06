@echo off
setlocal
rem Creates keys\license.lic for a user from the request key their program shows.
rem Needs the signing key (keys\license-private.key, never commit it) and the built KeyGen tool.
rem If KeyGen is somewhere else, update the KEYGEN path below.

set "KEYGEN=%~dp0..\tools\Automation.KeyGen\bin\Release\net48\Automation.KeyGen.exe"
set "PRIVATE=%~dp0license-private.key"
set "OUT=%~dp0license.lic"

if not exist "%PRIVATE%" goto :nokey
if not exist "%KEYGEN%" goto :nokeygen

set "REQ="
set "SENT="
set "EXP="
echo.
set /p "REQ=Request key from the user: "
set /p "SENT=Date the key was sent, UTC (YYYYMMDD): "
set /p "EXP=Expiry date (YYYYMMDD), leave blank for no expiry: "

if "%REQ%"=="" goto :missing
if "%SENT%"=="" goto :missing

echo.
if "%EXP%"=="" (
  "%KEYGEN%" issue --key "%PRIVATE%" --request "%REQ%" --sent "%SENT%" --out "%OUT%"
) else (
  "%KEYGEN%" issue --key "%PRIVATE%" --request "%REQ%" --sent "%SENT%" --expires "%EXP%" --out "%OUT%"
)
if errorlevel 1 goto :failed

echo.
echo Done. Send this file to the user: %OUT%
goto :end

:nokey
echo Cannot find the signing key: %PRIVATE%
goto :end

:nokeygen
echo Cannot find KeyGen: %KEYGEN%
echo Update the KEYGEN path at the top of this file, or build the KeyGen project first.
goto :end

:missing
echo The request key and the send date are required.
goto :end

:failed
echo.
echo No license was created. See the message above.

:end
echo.
pause
endlocal

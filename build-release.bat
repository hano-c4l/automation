@echo off
setlocal
rem Makes a clean build for testing in the Release folder. This is the ONE place the program is built for testing:
rem   Release\AutomationTest\        the program (AutomationTest.exe)
rem   Release\BrowserExtension\      the browser extension for the Web actions (load it unpacked in Chrome or Edge)
rem   Release\KeyGen\                the license tool (AutomationTest.KeyGen.exe + make-license.bat)
rem Files that belong to the user are kept across builds (they are put aside, the folder is rebuilt, then they are put back):
rem   Release\AutomationTest\Setting.ini and license.lic
rem   Release\KeyGen\license-private.key
rem Nothing secret is copied from the source tree. The bin and obj folders of the source tree are left alone.

cd /d "%~dp0"
set "OUT=%~dp0Release"
set "KEEP=%TEMP%\AutomationTest_release_keep"

echo Putting the user's files aside...
if exist "%KEEP%" rmdir /s /q "%KEEP%"
mkdir "%KEEP%\AutomationTest" "%KEEP%\KeyGen"
if exist "%OUT%\AutomationTest\Setting.ini" copy /y "%OUT%\AutomationTest\Setting.ini" "%KEEP%\AutomationTest\" >nul
if exist "%OUT%\AutomationTest\license.lic" copy /y "%OUT%\AutomationTest\license.lic" "%KEEP%\AutomationTest\" >nul
if exist "%OUT%\KeyGen\license-private.key" copy /y "%OUT%\KeyGen\license-private.key" "%KEEP%\KeyGen\" >nul

echo Removing the old Release folder...
if exist "%OUT%" rmdir /s /q "%OUT%"
if exist "%OUT%" goto :locked

echo Building AutomationTest...
dotnet build "src\AutomationTest.App\AutomationTest.App.csproj" -c Release --no-incremental -o "%OUT%\AutomationTest"
if errorlevel 1 goto :failed

echo Building KeyGen...
dotnet build "tools\AutomationTest.KeyGen\AutomationTest.KeyGen.csproj" -c Release --no-incremental -o "%OUT%\KeyGen"
if errorlevel 1 goto :failed

xcopy /e /i /y /q "extension\browser-bridge" "%OUT%\BrowserExtension" >nul
copy /y "keys\make-license.bat" "%OUT%\KeyGen\make-license.bat" >nul
del /q "%OUT%\AutomationTest\*.pdb" "%OUT%\KeyGen\*.pdb" "%OUT%\AutomationTest\*.deps.json" "%OUT%\KeyGen\*.deps.json" 2>nul

call :restore
echo.
echo Done: %OUT%
goto :end

:locked
echo Cannot remove %OUT%. Close any program that is running from it and try again.
call :restore
goto :end

:failed
echo.
echo The build failed. See the messages above.
call :restore
goto :end

rem Puts the user's files back (also after a failed build, so they are never lost).
:restore
if not exist "%OUT%\AutomationTest" mkdir "%OUT%\AutomationTest"
if not exist "%OUT%\KeyGen" mkdir "%OUT%\KeyGen"
if exist "%KEEP%\AutomationTest\Setting.ini" copy /y "%KEEP%\AutomationTest\Setting.ini" "%OUT%\AutomationTest\" >nul
if exist "%KEEP%\AutomationTest\license.lic" copy /y "%KEEP%\AutomationTest\license.lic" "%OUT%\AutomationTest\" >nul
if exist "%KEEP%\KeyGen\license-private.key" copy /y "%KEEP%\KeyGen\license-private.key" "%OUT%\KeyGen\" >nul
exit /b 0

:end
echo.
pause
endlocal

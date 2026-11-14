@echo off
setlocal
rem Makes a clean build for testing in the Release folder. This is the ONE place the program is built for testing:
rem   Release\AutomationTest\        the program (AutomationTest.exe)
rem   Release\AutomationWebHelper\  the browser extension for the Web actions (load it unpacked in Chrome or Edge)
rem   Release\AutomationKeyGen\      the license tool (AutomationTest.KeyGen.exe + make-license.bat)
rem Files that belong to the user are kept across builds (they are put aside, the folder is rebuilt, then they are put back):
rem   Release\AutomationTest\Setting.ini and license.lic
rem   Release\AutomationKeyGen\license-private.key (also taken over from the old Release\KeyGen folder)
rem The AutomationWebHelper folder is never deleted, only updated, so an extension loaded from it keeps working (Reload it after a build).
rem Nothing secret is copied from the source tree. The bin and obj folders of the source tree are left alone.

cd /d "%~dp0"
set "OUT=%~dp0Release"
set "KEEP=%TEMP%\AutomationTest_release_keep"

echo Putting the user's files aside...
if exist "%KEEP%" rmdir /s /q "%KEEP%"
mkdir "%KEEP%\AutomationTest" "%KEEP%\AutomationKeyGen"
if exist "%OUT%\AutomationTest\Setting.ini" copy /y "%OUT%\AutomationTest\Setting.ini" "%KEEP%\AutomationTest\" >nul
if exist "%OUT%\AutomationTest\license.lic" copy /y "%OUT%\AutomationTest\license.lic" "%KEEP%\AutomationTest\" >nul
rem The tool folder used to be called KeyGen: a key left there is kept as well.
if exist "%OUT%\KeyGen\license-private.key" copy /y "%OUT%\KeyGen\license-private.key" "%KEEP%\AutomationKeyGen\" >nul
if exist "%OUT%\AutomationKeyGen\license-private.key" copy /y "%OUT%\AutomationKeyGen\license-private.key" "%KEEP%\AutomationKeyGen\" >nul

echo Removing the old build (the AutomationWebHelper folder stays: a browser may have the extension loaded from it)...
for %%D in (AutomationTest AutomationKeyGen KeyGen BrowserExtension) do if exist "%OUT%\%%D" rmdir /s /q "%OUT%\%%D"
if exist "%OUT%\AutomationTest" goto :locked

echo Building AutomationTest...
dotnet build "src\AutomationTest.App\AutomationTest.App.csproj" -c Release --no-incremental -o "%OUT%\AutomationTest"
if errorlevel 1 goto :failed

echo Building KeyGen...
dotnet build "tools\AutomationTest.KeyGen\AutomationTest.KeyGen.csproj" -c Release --no-incremental -o "%OUT%\AutomationKeyGen"
if errorlevel 1 goto :failed

rem The extension folder is updated in place (files that are gone are removed), so a loaded extension keeps its path.
robocopy "extension\browser-bridge" "%OUT%\AutomationWebHelper" /MIR /NFL /NDL /NJH /NJS /NP >nul
copy /y "keys\make-license.bat" "%OUT%\AutomationKeyGen\make-license.bat" >nul
del /q "%OUT%\AutomationTest\*.pdb" "%OUT%\AutomationKeyGen\*.pdb" "%OUT%\AutomationTest\*.deps.json" "%OUT%\AutomationKeyGen\*.deps.json" 2>nul

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
if not exist "%OUT%\AutomationKeyGen" mkdir "%OUT%\AutomationKeyGen"
if exist "%KEEP%\AutomationTest\Setting.ini" copy /y "%KEEP%\AutomationTest\Setting.ini" "%OUT%\AutomationTest\" >nul
if exist "%KEEP%\AutomationTest\license.lic" copy /y "%KEEP%\AutomationTest\license.lic" "%OUT%\AutomationTest\" >nul
if exist "%KEEP%\AutomationKeyGen\license-private.key" copy /y "%KEEP%\AutomationKeyGen\license-private.key" "%OUT%\AutomationKeyGen\" >nul
exit /b 0

:end
echo.
pause
endlocal

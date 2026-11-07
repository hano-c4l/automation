@echo off
setlocal
rem Makes a clean build for users in the Release folder:
rem   Release\AutomationTest\   the program (AutomationTest.exe)
rem   Release\KeyGen\           the license tool (AutomationTest.KeyGen.exe + make-license.bat)
rem Nothing secret is copied: put license-private.key next to make-license.bat yourself.
rem The bin and obj folders of the source tree are left alone (they may hold a developer license).

cd /d "%~dp0"
set "OUT=%~dp0Release"

echo Removing the old Release folder...
if exist "%OUT%" rmdir /s /q "%OUT%"
if exist "%OUT%" goto :locked

echo Building AutomationTest...
dotnet build "src\AutomationTest.App\AutomationTest.App.csproj" -c Release --no-incremental -o "%OUT%\AutomationTest"
if errorlevel 1 goto :failed

echo Building KeyGen...
dotnet build "tools\AutomationTest.KeyGen\AutomationTest.KeyGen.csproj" -c Release --no-incremental -o "%OUT%\KeyGen"
if errorlevel 1 goto :failed

copy /y "keys\make-license.bat" "%OUT%\KeyGen\make-license.bat" >nul
del /q "%OUT%\AutomationTest\*.pdb" "%OUT%\KeyGen\*.pdb" 2>nul

echo.
echo Done: %OUT%
goto :end

:locked
echo Cannot remove %OUT%. Close any program that is running from it and try again.
goto :end

:failed
echo.
echo The build failed. See the messages above.

:end
echo.
pause
endlocal

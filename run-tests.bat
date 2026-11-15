@echo off
setlocal
rem Runs the tests of the program (tests\AutomationTest.Tests). They build into their own folders under tests\, never into Release.
rem   run-tests.bat                  every suite except the ones that start Excel
rem   run-tests.bat --excel          also the Excel suites (starts a hidden Excel of its own and quits it)
rem   run-tests.bat --only NAME      only the suites whose name contains NAME (for example --only editor)
rem   run-tests.bat --list           the names of the suites
rem Exit code 0 = everything passed.
cd /d "%~dp0"
dotnet run --project "tests\AutomationTest.Tests\AutomationTest.Tests.csproj" -c Debug -- %*
set "CODE=%ERRORLEVEL%"
endlocal & exit /b %CODE%

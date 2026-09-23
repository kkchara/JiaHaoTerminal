@echo off
setlocal
title JIAHAO // BUILD

rem Compiles with the C# compiler that ships with Windows.
rem No Visual Studio or .NET SDK required.
rem /codepage:65001 is required, otherwise Chinese text in the source becomes garbled.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :nocsc

set "SRC=%~dp0src\JiahaoBreach.cs"
set "OUT=%~dp0JiahaoBreach.exe"

if not exist "%SRC%" goto :nosrc

echo.
echo   ============================================
echo    JIAHAO-NET  //  BUILD
echo   ============================================
echo    compiler : %CSC%
echo    source   : %SRC%
echo    output   : %OUT%
echo.

"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu ^
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  "/out:%OUT%" "%SRC%"

if errorlevel 1 goto :failed

echo   [ OK ] Build succeeded.
for %%A in ("%OUT%") do echo          size: %%~zA bytes
echo.
echo   Double-click JiahaoBreach.exe to run.
pause
exit /b 0

:nocsc
echo [X] csc.exe not found. Windows .NET Framework 4.x is required.
pause
exit /b 1

:nosrc
echo [X] Source not found: %SRC%
pause
exit /b 1

:failed
echo.
echo [X] Build FAILED. See the errors above.
pause
exit /b 1

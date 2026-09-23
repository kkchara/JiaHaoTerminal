@echo off
setlocal
title JiaHao // LAUNCHER

set "PAGE=%~dp0index.html"
if not exist "%PAGE%" goto :nofile

rem --- isolated throwaway profile: keeps kiosk fullscreen even if Chrome is already open ---
set "PROFILE=%TEMP%\jiahao_kiosk_profile"

rem --- --autoplay-policy is what lets the intro sound play without a click ---
set "FLAGS=--kiosk --user-data-dir="%PROFILE%" --autoplay-policy=no-user-gesture-required --no-first-run --no-default-browser-check --disable-infobars --disable-session-crashed-bubble --overscroll-history-navigation=0 --disable-pinch"

set "CHROME="
if exist "%ProgramFiles%\Google\Chrome\Application\chrome.exe" set "CHROME=%ProgramFiles%\Google\Chrome\Application\chrome.exe"
if exist "%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe" set "CHROME=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"
if exist "%LocalAppData%\Google\Chrome\Application\chrome.exe" set "CHROME=%LocalAppData%\Google\Chrome\Application\chrome.exe"

set "EDGE="
if exist "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" set "EDGE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
if exist "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" set "EDGE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"

echo.
echo   ============================================
echo    JiaHao-NET  //  BREACH CONSOLE   v9.9.9
echo   ============================================
echo    file : %PAGE%
echo    exit : ESC leaves fullscreen  ^|  Alt+F4 closes
echo.

if defined CHROME goto :gochrome
if defined EDGE goto :goedge
goto :godefault

:gochrome
echo   [ OK ] engine: Chrome
start "" "%CHROME%" %FLAGS% "%PAGE%"
goto :eof

:goedge
echo   [ OK ] engine: Edge
start "" "%EDGE%" %FLAGS% "%PAGE%"
goto :eof

:godefault
echo   [ !! ] Chrome/Edge not found - opening in default browser.
echo          Press F11 there for fullscreen.
start "" "%PAGE%"
goto :eof

:nofile
echo [X] index.html not found. Keep this .bat in the same folder as index.html.
pause
exit /b 1

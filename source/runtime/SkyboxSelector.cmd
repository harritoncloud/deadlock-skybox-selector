@echo off
setlocal EnableExtensions
title Deadlock Skybox Selector

if not defined DEADLOCK_ROOT set "DEADLOCK_ROOT=C:\Program Files (x86)\Steam\steamapps\common\Deadlock"
if not defined SKYBOX_CACHE_ROOT set "SKYBOX_CACHE_ROOT=%DEADLOCK_ROOT%\patchwin.cc-skyboxes"

:startup
cls
echo ==================================================
echo             Deadlock Skybox Selector
echo ==================================================
echo Checking installation...
echo Cache: %SKYBOX_CACHE_ROOT%
echo.
call :run_selector status
set "status=%ERRORLEVEL%"

if "%status%"=="0" goto menu
if "%status%"=="11" goto menu
if not "%status%"=="10" goto status_error

echo.
set "install_choice="
set /p "install_choice=Skybox mod is not installed. Choose a skybox now? [Y/N]: "
if /I "%install_choice%"=="Y" goto menu
exit /b 0

:status_error
echo.
echo Installation status could not be verified.
echo The application will not overwrite unknown mod files.
pause
exit /b %status%

:menu
cls
echo ==================================================
echo             Deadlock Skybox Selector
echo ==================================================
set "current=vanilla / not installed"
if exist "%SKYBOX_CACHE_ROOT%\selected-skybox.txt" set /p current=<"%SKYBOX_CACHE_ROOT%\selected-skybox.txt"
echo Current: %current%
echo.
echo   1. Anime skyboxes       [13]
echo   2. Realistic skyboxes   [19]
echo   3. Vanilla
echo   4. GAMEINFO CONFIG INSTALLER (restored rendering, UAC)
echo.
echo   P. Open Anime previews
echo   R. Open Realistic previews
echo   G. Open GameInfo installer
echo   0. Exit
echo.
set "choice="
set /p "choice=Select [0-4, P, R or G]: "

if /I "%choice%"=="P" goto previews_anime
if /I "%choice%"=="R" goto previews_realistic
if /I "%choice%"=="G" goto gameinfo
if "%choice%"=="1" goto anime_menu
if "%choice%"=="2" goto realistic_menu
if "%choice%"=="3" set "skybox=vanilla"
if "%choice%"=="3" goto apply
if "%choice%"=="4" goto gameinfo
if "%choice%"=="0" exit /b 0
goto invalid

:anime_menu
cls
echo ==================================================
echo                 Anime Skyboxes
echo ==================================================
echo Select 01-13. Use P to open the preview sheet.
echo Enter B to return.
echo.
set "choice="
set /p "choice=Anime [01-13, P or B]: "
if /I "%choice%"=="P" goto previews_anime
if /I "%choice%"=="B" goto menu
set "skybox="
if "%choice%"=="1" set "skybox=anime_01"
if "%choice%"=="01" set "skybox=anime_01"
if "%choice%"=="2" set "skybox=anime_02"
if "%choice%"=="02" set "skybox=anime_02"
if "%choice%"=="3" set "skybox=anime_03"
if "%choice%"=="03" set "skybox=anime_03"
if "%choice%"=="4" set "skybox=anime_04"
if "%choice%"=="04" set "skybox=anime_04"
if "%choice%"=="5" set "skybox=anime_05"
if "%choice%"=="05" set "skybox=anime_05"
if "%choice%"=="6" set "skybox=anime_06"
if "%choice%"=="06" set "skybox=anime_06"
if "%choice%"=="7" set "skybox=anime_07"
if "%choice%"=="07" set "skybox=anime_07"
if "%choice%"=="8" set "skybox=anime_08"
if "%choice%"=="08" set "skybox=anime_08"
if "%choice%"=="9" set "skybox=anime_09"
if "%choice%"=="09" set "skybox=anime_09"
if "%choice%"=="10" set "skybox=anime_10"
if "%choice%"=="11" set "skybox=anime_11"
if "%choice%"=="12" set "skybox=anime_12"
if "%choice%"=="13" set "skybox=anime_13"
if not defined skybox goto invalid_anime
goto apply

:realistic_menu
cls
echo ==================================================
echo               Realistic Skyboxes
echo ==================================================
echo Select 01-19. Use P to open the preview sheet.
echo 19. Half-Life 2 Style
echo Enter B to return.
echo.
set "choice="
set /p "choice=Realistic [01-19, P or B]: "
if /I "%choice%"=="P" goto previews_realistic
if /I "%choice%"=="B" goto menu
set "skybox="
if "%choice%"=="1" set "skybox=realistic_01"
if "%choice%"=="01" set "skybox=realistic_01"
if "%choice%"=="2" set "skybox=realistic_02"
if "%choice%"=="02" set "skybox=realistic_02"
if "%choice%"=="3" set "skybox=realistic_03"
if "%choice%"=="03" set "skybox=realistic_03"
if "%choice%"=="4" set "skybox=realistic_04"
if "%choice%"=="04" set "skybox=realistic_04"
if "%choice%"=="5" set "skybox=realistic_05"
if "%choice%"=="05" set "skybox=realistic_05"
if "%choice%"=="6" set "skybox=realistic_06"
if "%choice%"=="06" set "skybox=realistic_06"
if "%choice%"=="7" set "skybox=realistic_07"
if "%choice%"=="07" set "skybox=realistic_07"
if "%choice%"=="8" set "skybox=realistic_08"
if "%choice%"=="08" set "skybox=realistic_08"
if "%choice%"=="9" set "skybox=realistic_09"
if "%choice%"=="09" set "skybox=realistic_09"
if "%choice%"=="10" set "skybox=realistic_10"
if "%choice%"=="11" set "skybox=realistic_11"
if "%choice%"=="12" set "skybox=realistic_12"
if "%choice%"=="13" set "skybox=realistic_13"
if "%choice%"=="14" set "skybox=realistic_14"
if "%choice%"=="15" set "skybox=realistic_15"
if "%choice%"=="16" set "skybox=realistic_16"
if "%choice%"=="17" set "skybox=realistic_17"
if "%choice%"=="18" set "skybox=realistic_18"
if "%choice%"=="19" set "skybox=realistic_19"
if not defined skybox goto invalid_realistic
goto apply

:apply
echo.
call :run_selector select "%skybox%"
set "result=%ERRORLEVEL%"
echo.
if not "%result%"=="0" echo Selection failed. No unknown VPK files were overwritten.
pause
goto menu

:run_selector
if /I "%~1"=="status" goto run_status
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0select-skybox.ps1" -Action select -Selection "%~2" -DeadlockRoot "%DEADLOCK_ROOT%" -CacheRoot "%SKYBOX_CACHE_ROOT%"
exit /b %ERRORLEVEL%

:run_status
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0select-skybox.ps1" -Action status -DeadlockRoot "%DEADLOCK_ROOT%" -CacheRoot "%SKYBOX_CACHE_ROOT%"
exit /b %ERRORLEVEL%

:gameinfo
if not exist "%~dp0DeadlockGameInfoInstaller.exe" (
    echo.
    echo GameInfo installer is missing.
    pause
    goto menu
)
echo.
"%~dp0DeadlockGameInfoInstaller.exe" --no-pause
set "result=%ERRORLEVEL%"
echo.
if not "%result%"=="0" echo GameInfo installer returned exit code %result%.
pause
goto menu

:previews_anime
if exist "%SKYBOX_CACHE_ROOT%\previews\anime-contact-sheet.jpg" (
    start "" "%SKYBOX_CACHE_ROOT%\previews\anime-contact-sheet.jpg"
) else (
    echo.
    echo Anime preview sheet is missing from the cache.
    pause
)
goto menu

:previews_realistic
if exist "%SKYBOX_CACHE_ROOT%\previews\realistic-contact-sheet.jpg" (
    start "" "%SKYBOX_CACHE_ROOT%\previews\realistic-contact-sheet.jpg"
) else (
    echo.
    echo Realistic preview sheet is missing from the cache.
    pause
)
goto menu

:invalid
echo.
echo Invalid selection.
pause
goto menu

:invalid_anime
echo.
echo Invalid Anime selection.
pause
goto anime_menu

:invalid_realistic
echo.
echo Invalid Realistic selection.
pause
goto realistic_menu

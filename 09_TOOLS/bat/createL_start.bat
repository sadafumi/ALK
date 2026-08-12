@echo off
set "PROJECT_ROOT=C:\DEV7\ALK"

if not exist "%PROJECT_ROOT%" (
    echo Creating directory %PROJECT_ROOT%...
    mkdir "%PROJECT_ROOT%"
)

echo Resetting L: drive mapping if exists...
subst l: /d >nul 2>&1

echo Mapping L: to C:\DEV7...
subst l: %PROJECT_ROOT%

if errorlevel 1 (
    echo.
    echo [ERROR] Failed to map L: drive.
    echo Please check if L: drive is already in use by a physical drive/network share,
    echo or if you have sufficient permissions.
    pause
) else (
    echo [SUCCESS] L: drive mapped successfully.
    timeout /t 3
)

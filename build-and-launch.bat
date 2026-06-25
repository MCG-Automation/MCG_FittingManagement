@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

set "PROJECT_NAME=MCG_FittingManagement"
set "AUTOCAD_EXE=C:\Program Files\Autodesk\AutoCAD 2023\acad.exe"

echo.
echo ===================================================
echo   MCG_FittingManagement -- Build ^& Launch AutoCAD
echo ===================================================
echo.
echo   Chon che do build:
echo   [1] Debug   (co timestamp, de kiem tra loi)
echo   [2] Release (ten co dinh, copy sang CustomTools)
echo.
set /p BUILD_CHOICE="Nhap lua chon (1 hoac 2): "

if "%BUILD_CHOICE%"=="1" (
    set "BUILD_CONFIG=Debug"
) else if "%BUILD_CHOICE%"=="2" (
    set "BUILD_CONFIG=Release"
) else (
    echo [LOI] Lua chon khong hop le. Thoat.
    pause
    exit /b 1
)

echo.
echo   Che do: %BUILD_CONFIG%
echo ===================================================
echo.

:: ==========================================
:: BUOC 1 -- Tat AutoCAD neu dang chay
:: ==========================================
echo [1/3] Kiem tra AutoCAD...
tasklist /FI "IMAGENAME eq acad.exe" 2>nul | find /I "acad.exe" >nul
if %ERRORLEVEL%==0 (
    echo       AutoCAD dang chay -- dang tat...
    taskkill /F /IM acad.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
    echo       AutoCAD da tat.
) else (
    echo       AutoCAD chua chay -- OK.
)
echo.

:: ==========================================
:: BUOC 2 -- Build project
:: (csproj tu dong copy DLL vao MCG_Plugin.bundle\Contents\MCG_FittingManagement.dll)
:: ==========================================
echo [2/3] Dang build %PROJECT_NAME% (%BUILD_CONFIG%)...
echo.
dotnet build -c %BUILD_CONFIG% --nologo
if %ERRORLEVEL% neq 0 (
    echo.
    echo [LOI] BUILD THAT BAI -- Kiem tra loi ben tren.
    echo       AutoCAD se KHONG duoc mo.
    echo.
    pause
    exit /b 1
)
echo.
echo       Build thanh cong. DLL da deploy vao MCG_Plugin.bundle.
echo.

:: ==========================================
:: BUOC 3 -- Mo AutoCAD
:: ==========================================
echo [3/3] Dang mo AutoCAD 2023...
if not exist "%AUTOCAD_EXE%" (
    echo [LOI] Khong tim thay AutoCAD tai:
    echo       %AUTOCAD_EXE%
    pause
    exit /b 1
)

start "" "%AUTOCAD_EXE%"

echo.
echo ===================================================
echo   HOAN THANH! [%BUILD_CONFIG%]
echo   Go lenh MCG_Fitting trong AutoCAD de kiem tra.
echo ===================================================
echo.
exit /b 0

@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo =====================================================
echo   MCGVN -- INSTALLING MCG_FittingManagement Plugin
echo =====================================================
echo.

:: ==========================================
:: 1. KHAI BAO BIEN
:: ==========================================
set "SOURCE_DIR=%~dp0"
set "BUNDLE_DIR=%PROGRAMDATA%\Autodesk\ApplicationPlugins\MCG_FittingManagement.bundle"
set "CONTENTS_DIR=%BUNDLE_DIR%\Contents"
set "XML_FILE=%BUNDLE_DIR%\PackageContents.xml"

:: ==========================================
:: 2. TAO THU MUC & KIEM TRA QUYEN ADMIN
:: ==========================================
echo [1/3] Tao cau truc thu muc Autodesk chuan...
if not exist "%CONTENTS_DIR%" mkdir "%CONTENTS_DIR%"

if not exist "%CONTENTS_DIR%" (
    echo.
    echo [LOI] Khong the tao thu muc:
    echo       %CONTENTS_DIR%
    echo.
    echo       Vui long chuot phai vao file .bat nay
    echo       va chon "Run as administrator".
    echo.
    pause
    exit /b 1
)
echo       OK: %CONTENTS_DIR%
echo.

:: ==========================================
:: 2.5 KIEM TRA FILE DLL TON TAI
:: ==========================================
echo [?] Kiem tra file DLL...
if not exist "%SOURCE_DIR%MCG_*.dll" (
    echo.
    echo [CANH BAO] Khong tim thay file "MCG_*.dll" tai:
    echo            %SOURCE_DIR%
    echo.
    echo       Dam bao file .bat nam cung thu muc voi cac file DLL,
    echo       va ten DLL phai bat dau bang "MCG_".
    echo.
    pause
    exit /b 1
)

set /a dll_count=0
for %%F in ("%SOURCE_DIR%MCG_*.dll") do set /a dll_count+=1
echo       Tim thay !dll_count! file MCG_*.dll -- OK
echo.

:: ==========================================
:: 3. COPY DLL VA APPSETTINGS VAO CONTENTS
:: ==========================================
echo [2/3] Copy MCG_*.dll vao Contents folder...
copy "%SOURCE_DIR%MCG_*.dll" "%CONTENTS_DIR%\" /Y >nul
if %ERRORLEVEL% neq 0 (
    echo [LOI] Copy DLL that bai. Kiem tra quyen ghi vao:
    echo       %CONTENTS_DIR%
    pause
    exit /b 1
)

for %%F in ("%SOURCE_DIR%MCG_*.dll") do (
    echo       [OK] %%~nxF
)

if exist "%SOURCE_DIR%appsettings.txt" (
    copy "%SOURCE_DIR%appsettings.txt" "%CONTENTS_DIR%\" /Y >nul
    echo       [OK] appsettings.txt
)
echo.

:: ==========================================
:: 4. TU SINH PACKAGECONTENTS.XML
:: ==========================================
echo [3/3] Tao PackageContents.xml...

(
echo ^<?xml version="1.0" encoding="utf-8"?>
echo ^<ApplicationPackage SchemaVersion="1.0" AppVersion="1.0.0" Author="MCG Team" Name="MCG_FittingManagement"^>
echo   ^<Components^>
echo     ^<RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="R24.0" /^>
) > "%XML_FILE%"

for %%F in ("%SOURCE_DIR%MCG_*.dll") do (
    echo       + %%~nxF
    (
    echo     ^<ComponentEntry AppName="%%~nF" Version="1.0.0" ModuleName="./Contents/%%~nxF" LoadOnAutoCADStartup="True" /^>
    ) >> "%XML_FILE%"
)

(
echo   ^</Components^>
echo ^</ApplicationPackage^>
) >> "%XML_FILE%"

echo.
echo =====================================================
echo   CAI DAT THANH CONG!
echo.
echo   Bundle: %BUNDLE_DIR%
echo.
echo   Khoi dong lai AutoCAD 2023 de ap dung.
echo   Go lenh MCG_Fitting de mo giao dien plugin.
echo =====================================================
echo.
pause

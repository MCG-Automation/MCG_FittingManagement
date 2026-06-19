@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ==========================================
:: CẤU HÌNH — Chỉnh tại đây nếu cần
:: ==========================================
set "PROJECT_NAME=MCG_FittingManagement"
set "AUTOCAD_EXE=C:\Program Files\Autodesk\AutoCAD 2023\acad.exe"
set "BUNDLE_DIR=%PROGRAMDATA%\Autodesk\ApplicationPlugins\MCG_FittingManagement.bundle"
set "CONTENTS_DIR=%BUNDLE_DIR%\Contents"

:: ==========================================
:: CHỌN CHẾ ĐỘ BUILD
:: ==========================================
echo.
echo ===================================================
echo   MCG_FittingManagement — Build ^& Launch AutoCAD
echo ===================================================
echo.
echo   Chon che do build:
echo   [1] Debug   (co timestamp, de kiem tra loi)
echo   [2] Release (ten co dinh, dung de deploy)
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

:: Đường dẫn output sau build
set "BUILD_OUTPUT=bin\%BUILD_CONFIG%"
set "DLL_NAME_FILE=%BUILD_OUTPUT%\_current_dll_name.txt"

:: ==========================================
:: BƯỚC 1 — Kiểm tra AutoCAD đang chạy
:: ==========================================
echo [1/5] Kiem tra AutoCAD dang chay...
tasklist /FI "IMAGENAME eq acad.exe" 2>nul | find /I "acad.exe" >nul
if %ERRORLEVEL%==0 (
    echo       AutoCAD dang chay -- dang tat de release .dll...
    taskkill /F /IM acad.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
    echo       AutoCAD da tat.
) else (
    echo       AutoCAD chua chay -- OK.
)
echo.

:: ==========================================
:: BƯỚC 2 — Build project
:: ==========================================
echo [2/5] Dang build %PROJECT_NAME% (%BUILD_CONFIG%)...
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
echo       Build thanh cong.
echo.

:: ==========================================
:: BƯỚC 3 — Đọc tên DLL thật từ file do MSBuild ghi ra
:: ==========================================
echo [3/5] Doc ten DLL...

:: .csproj tự ghi tên DLL vào _current_dll_name.txt sau mỗi build
if not exist "%DLL_NAME_FILE%" (
    echo [LOI] Khong tim thay file: %DLL_NAME_FILE%
    echo       Kiem tra lai cau hinh MSBuild trong .csproj
    pause
    exit /b 1
)

:: Đọc tên DLL từ file (bỏ khoảng trắng thừa)
set /p DLL_BASE_NAME=<"%DLL_NAME_FILE%"
set "DLL_BASE_NAME=%DLL_BASE_NAME: =%"
set "DLL_FILE=%DLL_BASE_NAME%.dll"
set "DLL_SOURCE=%BUILD_OUTPUT%\%DLL_FILE%"

echo       Ten DLL: %DLL_FILE%

if not exist "%DLL_SOURCE%" (
    echo [LOI] Khong tim thay DLL: %DLL_SOURCE%
    pause
    exit /b 1
)
echo       DLL tim thay -- OK.
echo.

:: ==========================================
:: BƯỚC 4 — Copy .dll vào bundle folder
:: ==========================================
echo [4/5] Dang copy plugin vao bundle folder...

:: Tạo bundle folder nếu chưa có
if not exist "%CONTENTS_DIR%" (
    mkdir "%CONTENTS_DIR%"
    if not exist "%CONTENTS_DIR%" (
        echo [LOI] Khong the tao thu muc. Chay lai voi quyen Administrator.
        pause
        exit /b 1
    )
    echo       Bundle folder da tao: %BUNDLE_DIR%
)

:: Debug: Xóa các DLL timestamp cũ trước khi copy DLL mới
:: (tránh chồng chất nhiều file DLL cũ trong Contents)
if "%BUILD_CONFIG%"=="Debug" (
    echo       [Debug] Dang xoa cac DLL cu co timestamp...
    for %%f in ("%CONTENTS_DIR%\%PROJECT_NAME%_*.dll") do (
        del "%%f" >nul 2>&1
    )
)

:: Copy DLL mới
copy /Y "%DLL_SOURCE%" "%CONTENTS_DIR%\" >nul
if %ERRORLEVEL% neq 0 (
    echo [LOI] Copy .dll that bai.
    pause
    exit /b 1
)

:: Copy appsettings.txt nếu có
if exist "%~dp0appsettings.txt" (
    copy /Y "%~dp0appsettings.txt" "%CONTENTS_DIR%\" >nul
    echo       appsettings.txt da copy.
)

:: Lần đầu deploy: copy PackageContents.xml từ project root vào bundle root
set "PKG_SRC=%~dp0PackageContents.xml"
set "PKG_DST=%BUNDLE_DIR%\PackageContents.xml"
if exist "%PKG_SRC%" (
    if not exist "%PKG_DST%" (
        copy /Y "%PKG_SRC%" "%BUNDLE_DIR%\" >nul
        echo       PackageContents.xml da copy vao bundle (lan dau).
    ) else (
        echo       PackageContents.xml da duoc MSBuild cap nhat.
    )
) else (
    echo [CANH BAO] Khong tim thay PackageContents.xml -- AutoCAD co the khong load plugin.
)

echo       Plugin da copy: %CONTENTS_DIR%\%DLL_FILE%
echo.

:: ==========================================
:: BƯỚC 5 — Mở AutoCAD
:: ==========================================
echo [5/5] Dang mo AutoCAD 2023...
if not exist "%AUTOCAD_EXE%" (
    echo [LOI] Khong tim thay AutoCAD tai:
    echo       %AUTOCAD_EXE%
    pause
    exit /b 1
)

start "" "%AUTOCAD_EXE%"

echo.
echo ===================================================
if "%BUILD_CONFIG%"=="Debug" (
    echo   [DEBUG] HOAN THANH!
    echo   DLL: %DLL_FILE%
    echo   AutoCAD dang khoi dong voi plugin moi nhat.
) else (
    echo   [RELEASE] HOAN THANH!
    echo   DLL: %DLL_FILE%
    echo   AutoCAD dang khoi dong voi ban Release.
)
echo   Go lenh MCG_Fitting trong AutoCAD de kiem tra.
echo ===================================================
echo.
exit /b 0
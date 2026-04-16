@echo off
chcp 65001 >nul
echo ========================================
echo PhotoBooth 部署環境檢查工具
echo ========================================
echo.

echo [1/4] 檢查 Windows 版本...
for /f "tokens=4-5 delims=. " %%i in ('ver') do set VERSION=%%i.%%j
echo Windows 版本: %VERSION%
if "%VERSION%" LSS "10.0" (
    echo ⚠ 警告：建議使用 Windows 10 或更高版本
) else (
    echo ✓ Windows 版本符合要求
)
echo.

echo [2/4] 檢查 WebView2 Runtime...
reg query "HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo ✓ WebView2 Runtime 已安裝
) else (
    echo ✗ WebView2 Runtime 未安裝
    echo.
    echo 請下載並安裝 WebView2 Runtime：
    echo https://developer.microsoft.com/microsoft-edge/webview2/
    echo.
    set NEED_WEBVIEW2=1
)
echo.

echo [3/4] 檢查 COM 口...
echo 可用的 COM 口：
wmic path Win32_SerialPort get DeviceID,Description 2>nul | findstr /V "DeviceID Description" | findstr /V "^$"
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ 未找到 COM 口，請確認：
    echo   1. USB 轉串口設備已連接
    echo   2. USB 驅動已安裝
    echo   3. 在裝置管理員中檢查 COM 口
) else (
    echo ✓ 找到 COM 口
)
echo.

echo [4/4] 檢查管理員權限...
net session >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo ✓ 目前以管理員身份運行
) else (
    echo ⚠ 目前不是以管理員身份運行
    echo   建議以管理員身份運行應用程式（串口訪問需要權限）
)
echo.

echo ========================================
echo 檢查完成
echo ========================================
echo.

if defined NEED_WEBVIEW2 (
    echo ⚠ 需要安裝 WebView2 Runtime
    echo.
    echo 是否要打開下載頁面？(Y/N)
    set /p OPEN="> "
    if /i "%OPEN%"=="Y" (
        start https://developer.microsoft.com/microsoft-edge/webview2/
    )
) else (
    echo ✓ 所有檢查項目都通過
    echo   可以開始部署應用程式了
)
echo.

pause

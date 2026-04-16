@echo off
chcp 65001 >nul
echo ========================================
echo 檢查發布目錄中的資源文件
echo ========================================
echo.

set PUBLISH_DIR=PhotoBoothWin\bin\Release\net10.0-windows\win-x64\publish

if not exist "%PUBLISH_DIR%" (
    echo ✗ 發布目錄不存在：%PUBLISH_DIR%
    echo   請先執行 publish.bat 進行發布
    pause
    exit /b 1
)

echo 檢查目錄結構...
echo.

echo [1] 檢查 web 資料夾...
if exist "%PUBLISH_DIR%\web" (
    echo ✓ web 資料夾存在
) else (
    echo ✗ web 資料夾不存在！
    pause
    exit /b 1
)

echo [2] 檢查 web\assets 資料夾...
if exist "%PUBLISH_DIR%\web\assets" (
    echo ✓ web\assets 資料夾存在
) else (
    echo ✗ web\assets 資料夾不存在！
    pause
    exit /b 1
)

echo [3] 檢查模板圖片...
if exist "%PUBLISH_DIR%\web\assets\templates\chooselayout\bk01.png" (
    echo ✓ 選版型圖片存在
) else (
    echo ✗ 選版型圖片不存在！
)

echo [4] 檢查音效文件...
if exist "%PUBLISH_DIR%\web\assets\templates\music\倒數10秒拍照.mp3" (
    echo ✓ 倒數音效文件存在
) else (
    echo ✗ 倒數音效文件不存在！
)

echo [5] 檢查拍攝頁面資源...
if exist "%PUBLISH_DIR%\web\assets\templates\ShootPage\background.png" (
    echo ✓ 拍攝頁面背景存在
) else (
    echo ✗ 拍攝頁面背景不存在！
)

echo [6] 檢查結果頁面資源...
if exist "%PUBLISH_DIR%\web\assets\templates\QRcodePage\bk01.png" (
    echo ✓ 結果頁面模板存在
) else (
    echo ✗ 結果頁面模板不存在！
)

echo.
echo ========================================
echo 檢查完成
echo ========================================
echo.
echo 如果發現缺少文件，請：
echo 1. 確認源文件存在於 PhotoBoothWin\web\assets\ 目錄中
echo 2. 重新執行 publish.bat
echo 3. 檢查項目配置中的 Content Include 設定
echo.

pause

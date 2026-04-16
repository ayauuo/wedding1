@echo off
echo ========================================
echo 打包 PhotoBooth 為自包含 exe
echo ========================================
echo.

cd PhotoBoothWin

echo 正在建置 Vue 前端 (web-vue/app)...
cd web-vue\app
call npm ci
call npm run build
cd ..\..

echo 正在發布應用程式...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo 發布成功！
    echo ========================================
    echo.
    echo 發布文件位置：
    echo PhotoBoothWin\bin\Release\net10.0-windows\win-x64\publish\
    echo.
    echo 檢查資源文件...
    if exist "PhotoBoothWin\bin\Release\net10.0-windows\win-x64\publish\web\assets" (
        echo ✓ web\assets 資料夾存在
    ) else (
        echo ✗ 警告：web\assets 資料夾不存在！
        echo   請確認資源文件是否正確複製
    )
    echo.
    echo 您可以將整個 publish 資料夾複製到其他電腦使用
    echo 不需要安裝 .NET Runtime！
    echo.
    echo 重要：請確認 publish 資料夾中包含以下內容：
    echo   - PhotoBoothWin.exe
    echo   - web\ 資料夾（包含所有 HTML、CSS、JS 和 assets）
    echo.
) else (
    echo.
    echo ========================================
    echo 發布失敗！
    echo ========================================
    echo 請檢查錯誤訊息
    echo.
)

pause

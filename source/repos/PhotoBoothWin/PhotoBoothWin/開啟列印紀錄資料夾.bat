@echo off
cd /d "%~dp0"
if exist "bin\Debug\net10.0-windows\win-x64\report" (
  start "" "bin\Debug\net10.0-windows\win-x64\report"
) else if exist "bin\Release\net10.0-windows\win-x64\report" (
  start "" "bin\Release\net10.0-windows\win-x64\report"
) else (
  if not exist "report" mkdir report
  start "" "report"
  echo 尚未執行過 WPF 列印，report 資料夾為空。請先從 Visual Studio 執行主程式並觸發列印，CSV 會出現在 bin\Debug\net10.0-windows\win-x64\report\
)
pause

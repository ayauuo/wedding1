# PhotoBooth 部署說明

## 打包請用 Visual Studio（或 publish.bat）

**請勿在 Cursor 或其他編輯器內直接執行 dotnet publish / 打包。** 請使用 **Visual Studio** 發布，或在本機雙擊執行 **publish.bat**，以確保發布流程與輸出正確。

## 自包含部署（不需要安裝 .NET Runtime）

本專案已配置為自包含部署，打包後的應用程式可以在沒有安裝 .NET Runtime 的 Windows 電腦上運行。

## 打包步驟

### 方法 1：使用批處理文件（推薦）

1. 雙擊執行 `publish.bat`
2. 等待打包完成
3. 打包文件位於：`PhotoBoothWin\bin\Release\net10.0-windows\win-x64\publish\`

### 方法 2：使用 Visual Studio

1. 右鍵點擊專案 → **發布 (Publish)**
2. 選擇 **資料夾 (Folder)**
3. 點擊 **完成 (Finish)**
4. 在發布設定中：
   - **部署模式**：選擇「自包含 (Self-contained)」
   - **目標運行時**：選擇「win-x64」
   - **單一文件**：勾選「產生單一文件 (Produce single file)」
5. 點擊 **發布 (Publish)**

### 方法 3：使用命令列

在專案根目錄執行：

```bash
cd PhotoBoothWin
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 部署到其他電腦

1. 將整個 `publish` 資料夾複製到目標電腦
2. 不需要安裝任何 .NET Runtime 或依賴項
3. 直接運行 `PhotoBoothWin.exe` 即可

## 注意事項

### WebView2 Runtime

雖然不需要 .NET Runtime，但應用程式需要 **WebView2 Runtime** 才能運行。

如果目標電腦沒有安裝 WebView2 Runtime，有兩種解決方案：

#### 方案 1：安裝 WebView2 Runtime（推薦）
- 下載並安裝：https://developer.microsoft.com/microsoft-edge/webview2/
- 這是 Microsoft 提供的免費運行時

#### 方案 2：使用固定版本運行時（進階）
- 在發布時包含 WebView2 運行時
- 需要修改發布配置，文件會更大

### 串口驅動

如果使用 USB 轉串口設備，目標電腦需要安裝對應的 USB 驅動程式。

### 前端路徑（web 資料夾）

- **預設**：程式從 **exe 所在資料夾下的 `web` 資料夾**載入前端（index.html、assets 等）。
- **改為指向其他路徑**（不必在 exe 旁放 web 資料夾）：
  1. **環境變數**：設定 `PHOTOBOOTH_WEB_ROOT` 為前端資料夾的完整路徑（例如 `C:\PhotoBooth\web`）。
  2. **設定檔**：在 exe 旁建立 `web_root.txt`，第一行寫前端資料夾的完整路徑。
- 若路徑不存在或未設定，則沿用預設（exe 旁 `web`）。執行時可在 Visual Studio 輸出視窗看到 `[WebView] 前端路徑: ...` 確認實際使用的路徑。

## 文件結構

打包後的 `publish` 資料夾包含：

```
publish/
├── PhotoBoothWin.exe          # 主程式（自包含，包含所有依賴）
├── web/                        # 前端文件
│   ├── index.html
│   ├── app.js
│   ├── styles.css
│   └── assets/
├── report/                     # 列印紀錄（首次列印或寫入紀錄時自動建立）
│   └── print_log.csv
└── Logs/                       # 日誌文件（運行時自動創建）
```

## 列印紀錄 CSV

- **何時產生**：只有執行 **WPF 主程式**（PhotoBoothWin.exe 或從 Visual Studio 執行）時，在結果頁按「列印」或 60 秒自動列印後，C# 才會寫入一筆紀錄。若只開瀏覽器跑 Vue（例如 `npm run dev`），不會產生 CSV。
- **路徑**：
  - **從 Visual Studio 執行**：在專案輸出目錄下，例如  
    `PhotoBoothWin\bin\Debug\net10.0-windows\win-x64\report\print_log.csv`
  - **執行已發布的 exe**：在 exe 所在資料夾下的 `report\print_log.csv`
- **除錯**：執行 WPF 並觸發列印後，在 Visual Studio 的「輸出」視窗可看到 `[列印紀錄] 已寫入: <完整路徑>`，即可確認檔案位置。若寫入失敗會顯示錯誤訊息。

## 故障排除

### 問題：無法運行 exe

1. 檢查是否以管理員身份運行（串口訪問需要權限）
2. 檢查是否安裝了 WebView2 Runtime
3. 查看 `Logs` 資料夾中的日誌文件

### 問題：無法連接紙鈔機

1. 檢查串口是否正確連接
2. 檢查設備管理器中的 COM 口號
3. 查看 `Logs/BillAcceptor_YYYYMMDD.log` 日誌文件
4. 確認以管理員身份運行

### 問題：文件太大

自包含部署會包含整個 .NET Runtime，文件大小約為 100-200 MB。這是正常的，因為不需要目標電腦安裝任何依賴。

## 技術細節

- **目標框架**：.NET 10.0
- **運行時**：win-x64
- **部署模式**：自包含 (Self-contained)
- **單一文件**：是
- **ReadyToRun**：啟用（提升啟動速度）
- **壓縮**：啟用（減小文件大小）

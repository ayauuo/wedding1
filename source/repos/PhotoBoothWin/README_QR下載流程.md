# QR Code 下載流程說明

掃描拍貼機結果頁的 QR code 後，使用者可進入下載頁取得相片／影片。本文說明完整流程與所需元件。

## 一、觸發時機：`buildFinalOutput()`

在 `usePhotobooth.ts` 的 `buildFinalOutput()` 中，合成完成後會依序執行：

1. **儲存合成圖**：`callHost('save_image', { imageData })` → 存到本機 `CaptureOutputDirectory`
2. **上傳合成圖**：`callHost('upload_file', { filePath })` → 取得圖片 URL
3. **若有影片**：`callHost('upload_video', { videoData })` → 取得影片 URL
4. **組下載頁網址**：`${basePage}?img=${imageUrl}&video=${videoUrl}`
5. **產生 QR code**：`QRCode.toDataURL(qrUrl, { width: 600, margin: 2 })`

## 二、上傳實作：兩種路徑

### A. 在 WebView 內（C# 代為上傳）

| 指令 | C# 處理 | 說明 |
|------|---------|------|
| `upload_file` | 若有 `s3_config.txt` → **AWS S3**；否則 → `UploadToServerAsync` → **PHP API** | 合成圖上傳 |
| `upload_video` | `BoothBridge.cs` → `UploadToServerAsync` → **PHP API** | 影片上傳 |

**S3 設定**：在 exe 同目錄（或專案根目錄）放 `s3_config.txt`，格式如下。若存在則 `upload_file` 會上傳到 S3 並回傳 24 小時有效的 presigned URL；S3 失敗時會自動改走 PHP API。
```
AccessKey=你的AccessKey
SecretKey=你的SecretKey
Bucket=你的bucket名稱
Region=ap-northeast-1
```

### B. 無 WebView（純網頁）

在 `useHost.ts` 中，若偵測不到 WebView，會用 mock 回傳，不會真的上傳：

- `upload_file` → `{ url: 'https://example.com/download/mock.jpg' }`
- `upload_video` → `{ url: 'https://example.com/download/mock.mp4' }`

若設定了 `VITE_UPLOAD_URL`，`upload` 與 `upload_video` 會改走 HTTP 直接 POST 到該網址。

## 三、環境與設定

| 項目 | 說明 |
|------|------|
| **下載頁基底網址** | `.env` 的 `VITE_DOWNLOAD_PAGE_BASE_URL`（例如 `https://guolicom.net/download`） |
| **上傳 API** | C# 從 `upload_url.txt` 或 `BoothBridge.UploadUrl` 讀取；Vue 純網頁模式用 `VITE_UPLOAD_URL` |
| **QR code 開關** | `VITE_QRCODE_ENABLED=0` 或 `false` 時隱藏 QR、不執行上傳；預設 `1` |
| **離線模式** | `VITE_OFFLINE_MODE=1` 時，不上傳、不產生 QR code（需在程式內實作此邏輯） |

## 四、結果頁顯示 QR code

`ScreenResult.vue` 會顯示 QR code 區塊，`qrDisplayUrl` 來自 `usePhotobooth` 的 `qrImageUrl`（即 `QRCode.toDataURL()` 產生的 data URL）。

- 有合成圖時：顯示真實 QR（指向 `basePage?img=...&video=...`）
- 無合成圖時：依 `VITE_RESULT_SHOW_TEMPLATE_PLACEHOLDER` 決定是否顯示占位 QR

## 五、流程整理

```
合成完成
  → showScreen('uploading')     // 顯示「照片上傳中」
  → save_image(imageData)      // 存到 CaptureOutputDirectory
  → upload_file(filePath)       // C# 讀檔 → POST 到 PHP API → 取得 url
  → [若有影片] upload_video(videoData)  // C# → PHP API → 取得 videoUrl
  → 組 qrUrl = basePage?img=...&video=...
  → QRCode.toDataURL(qrUrl)
  → showScreen('result')       // 顯示結果頁與 QR
```

## 六、必要元件檢查清單

### 1. 前端環境變數（`.env`）

| 變數 | 用途 | 範例 |
|------|------|------|
| `VITE_DOWNLOAD_PAGE_BASE_URL` | QR 指向的下載頁基底網址 | `https://guolicom.net/download` |
| `VITE_UPLOAD_URL` | 純網頁模式時，前端直接 POST 的上傳 API | `https://your-server.com/api/upload.php` |

### 2. C# 主機端（WebView 模式）

| 項目 | 說明 |
|------|------|
| `upload_url.txt` | 放在 exe 同目錄，內容一行為上傳 API 網址，會覆蓋 `BoothBridge.UploadUrl` |
| `BoothBridge.UploadUrl` | 預設 `https://www.guoli-tw.com/upload_model/api/upload.php` |
| `BoothBridge.CaptureOutputDirectory` | `save_image` 存檔目錄，需可寫入 |

### 3. 上傳 API（PHP）

`upload.php` 需符合：

- **Method**: POST
- **Content-Type**: application/json
- **Body**: `{ "imageData": "data:image/...;base64,...", "videoData": "data:video/...;base64,..." }`（擇一或同時）
- **Response**: `{ "url": "https://...", "videoUrl": "https://..." }`

### 4. 下載頁（download-page）

- 部署到與 `VITE_DOWNLOAD_PAGE_BASE_URL` 對應的網址
- 需能讀取 `?img=...&video=...` 並顯示相片／影片與下載按鈕
- `script.js` 已實作 `img`、`video` 參數解析

### 5. 依賴套件

- `qrcode`：`QRCode.toDataURL()` 產生 QR code

## 七、相關檔案

| 角色 | 檔案 |
|------|------|
| Vue 合成與 QR | `web-vue/app/src/composables/usePhotobooth.ts`：`buildFinalOutput`、`qrImageUrl` |
| Vue 主機呼叫 | `web-vue/app/src/composables/useHost.ts`：`callHost` |
| C# Bridge | `PhotoBoothWin/Bridge/BoothBridge.cs`：`upload_file`、`upload_video`、`UploadToServerAsync` |
| 結果頁 | `web-vue/app/src/components/photobooth/ScreenResult.vue` |
| 下載頁 | `web-vue/app/download-page/` |
| 上傳 API | `web-vue/app/api/upload.php` |

## 八、無 QR 結果頁（VITE_QRCODE_ENABLED=0）

當 `VITE_QRCODE_ENABLED=0` 或 `false` 時，合成完成後會進入 `result-no-qr` 畫面（`ScreenResultNoQr.vue`），使用 `NoQRcodePage` 素材：

- 路徑：`public/assets/templates/NoQRcodePage/`
- 素材：`bk01.png`～`bk04.png`、`cancelbutton.png`、`printbutton.png`
- 背景：`background.png`（若無則顯示背景色；可從 `QRcodePage` 複製）

畫面含「取消」與「列印」按鈕，無 QR code、不上傳。

## 九、尚未實作（可選）

- **VITE_OFFLINE_MODE**：離線模式時略過上傳與 QR，需在 `buildFinalOutput()` 內加入判斷

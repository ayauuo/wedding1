# 上傳 API（PHP）

拍貼機上線到 **server** 時，若沒有 C# WPF 的 WebView，前端會改走 **HTTP 上傳**：把合成圖（base64）POST 到你設定的 `VITE_UPLOAD_URL`，由後端存檔並回傳可下載網址。

## 使用方式

1. **部署 PHP**  
   將 `upload.php` 放到你的 server 可執行 PHP 的目錄（例如 `https://your-domain.com/api/upload.php`）。

2. **建立存檔目錄**  
   在 `upload.php` 同層建立 `uploads/` 目錄，並設定可寫入（例如 `chmod 755 uploads`）。  
   若目錄不存在，腳本會嘗試自動建立。

3. **設定 .env**  
   前端專案 `.env` 中設定：
   ```env
   VITE_UPLOAD_URL=https://your-domain.com/api/upload.php
   ```
   建置後，前端在「無 WebView」時會把合成圖以 POST JSON 送至此網址。

4. **下載頁基底網址**  
   掃 QR 後的下載頁網址請一併設定：
   ```env
   VITE_DOWNLOAD_PAGE_BASE_URL=https://your-domain.com/download
   ```
   回傳的 `url` / `videoUrl` 會用來組出 QR 的查詢參數。

## API 規格

- **Method**: `POST`
- **Content-Type**: `application/json`
- **Body**（擇一或同時）:
  - `imageData`: `"data:image/jpeg;base64,..."`（合成圖）
  - `videoData`: `"data:video/mp4;base64,..."`（錄影）
- **Response**: `{ "url": "https://...", "videoUrl": "https://..." }`  
  僅傳圖時回傳 `url`；僅傳影片時回傳 `url`（影片網址）與 `videoUrl`。

## 注意

- 需開啟 PHP 的 `file_get_contents('php://input')` 與 `json_decode`。
- 若前後端不同網域，`upload.php` 已加 CORS 表頭；若仍被擋，請在 server 再設定 CORS。
- 上傳檔案會存到 `api/uploads/`，請確保該目錄可被網頁讀取（或改為你實際的公開路徑），回傳的 URL 才會可下載。

# 上傳圖片／影片邏輯說明（C# 代為上傳到網頁）

Vue 前端在 WebView 內時，**上傳由 C# 代為執行**：Vue 透過 Bridge 傳給 C#，C# 用 HttpClient POST 到你的 PHP API，再把回傳的下載網址回給 Vue。

## 流程

1. **Vue**（`usePhotobooth` / `useHost`）  
   - 合成圖：`callHost('save_image', { imageData })` 存檔後，`callHost('upload_file', { filePath })` 上傳。  
   - 影片：`callHost('upload_video', { videoData: videoDataUrl })`  
   - `videoDataUrl` 為 `data:video/...;base64,...` 格式。

2. **C#**（`BoothBridge.cs`）  
   - 收到 `upload_file`：從本機讀取 `filePath` 檔案，轉成 base64，POST `{ "imageData": "data:image/...;base64,..." }` 到上傳 API。  
   - 收到 `upload_video`：從 `data.videoData` 取出 base64，POST `{ "videoData": "..." }` 到上傳 API。  
   - 使用 `HttpClient` 非同步上傳（`UploadToServerAsync`），不阻塞 UI。  
   - 解析 API 回傳的 JSON（`url`、`videoUrl`），再以 RPC 回傳 `{ id, ok, data: { url } }` 給 Vue。

3. **上傳 API**  
   - 與 Vue 的 `VITE_UPLOAD_URL` 對應，預設為：  
     `https://www.guoli-tw.com/upload_model/api/upload.php`  
   - 格式與 web-vue 的 `api/upload.php` 一致：POST JSON，回傳 `{ "url": "...", "videoUrl": "..." }`。

## 上傳網址設定

- **預設**：程式內建上述 guoli 網址。  
- **自訂**：在 **PhotoBoothWin.exe 所在目錄** 放一個 `upload_url.txt`，內容一行，例如：  
  `https://你的網域/api/upload.php`  
  執行時 C# 會讀取並改用此網址。

## 相關檔案

| 角色 | 檔案 |
|------|------|
| C# Bridge（上傳實作） | `PhotoBoothWin/Bridge/BoothBridge.cs`：`upload_file`、`upload_video`、`UploadToServerAsync`、`HandleAsync` |
| WPF 訊息處理 | `PhotoBoothWin/MainWindow.xaml.cs`：`WebMessageReceived` 改為 `async`，呼叫 `await _bridge.HandleAsync(msg)` |
| Vue 呼叫端 | `web-vue/app/src/composables/useHost.ts`（`callHost`）、`usePhotobooth.ts`（`buildFinalOutput` 內 `upload_file` / `upload_video`） |

## 延伸閱讀

- [README_QR下載流程.md](README_QR下載流程.md)：QR code 產生、下載頁網址組成、部署檢查清單

## 注意

- 打包專案請使用 **Visual Studio** 或 **publish.bat**，不要在 Cursor 內執行 dotnet publish。

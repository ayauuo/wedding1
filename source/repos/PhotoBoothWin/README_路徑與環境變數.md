# 路徑與環境變數設定

本專案可透過 **環境變數** 或 **程式目錄下的設定檔** 覆寫路徑，修改後不需重新編譯即可生效。

## 本地列印儲存路徑（DNP Hot Folder）

圖片列印時會複製到 **Prints** 目錄，預設為 `C:\DNP\Hot Folder\Prints`。若要改路徑，可用下列任一方式。

### 方式一：環境變數（建議用於本機或部署腳本）

在 **系統環境變數** 或 **使用者環境變數** 中新增：

| 環境變數 | 說明 | 預設值 |
|----------|------|--------|
| `PHOTOBOOTH_PRINTS_PATH` | 本地列印輸出目錄（Prints） | `C:\DNP\Hot Folder\Prints` |
| `DNP_HOT_FOLDER_EXE` | Hot Folder.exe 完整路徑 | `C:\DNP\Hot Folder\Hot Folder.exe` |

**Windows 設定步驟：**

1.  Win + R → 輸入 `sysdm.cpl` → 確定  
2. 「進階」→「環境變數」  
3. 在「使用者變數」或「系統變數」按「新增」  
4. 變數名稱：`PHOTOBOOTH_PRINTS_PATH`  
5. 變數值：例如 `D:\MyPrints` 或 `C:\DNP\Hot Folder\Prints`  
6. 確定後 **重新啟動 PhotoBoothWin** 才會讀到新值  

### 方式二：設定檔（建議用於每台機器不同路徑）

在 **PhotoBoothWin.exe 所在目錄** 建立 `hot_folder_path.txt`：

- **第 1 行**：Prints 目錄路徑（本地儲存圖片的路徑）  
- **第 2 行**（可選）：Hot Folder.exe 完整路徑  

範例：

```text
C:\DNP\Hot Folder\Prints
C:\DNP\Hot Folder\Hot Folder.exe
```

若只改 Prints 路徑，可只寫第 1 行：

```text
D:\MyPhotoBooth\Prints
```

**優先順序**：環境變數 > `hot_folder_path.txt` > 程式內建預設值。

## 上傳 API 網址

上傳圖片／影片的 API 網址可於程式目錄放 `upload_url.txt`（一行網址）覆寫，詳見 [README_上傳邏輯.md](README_上傳邏輯.md)。  
若使用 **AWS S3** 上傳合成圖，請在 exe 同目錄放 `s3_config.txt`（格式見 `s3_config.example.txt`），該檔已加入 `.gitignore` 不會被提交。  
QR code 下載頁基底網址與完整流程見 [README_QR下載流程.md](README_QR下載流程.md)。

## 總結

| 用途 | 環境變數 | 設定檔 | 預設值 |
|------|----------|--------|--------|
| 本地列印儲存路徑（Prints） | `PHOTOBOOTH_PRINTS_PATH` | `hot_folder_path.txt` 第 1 行 | `C:\DNP\Hot Folder\Prints` |
| Hot Folder.exe 路徑 | `DNP_HOT_FOLDER_EXE` | `hot_folder_path.txt` 第 2 行 | `C:\DNP\Hot Folder\Hot Folder.exe` |
| 上傳 API 網址 | — | `upload_url.txt` | 程式內建網址 |

修改路徑後，只要存檔（或設好環境變數並重開程式），即可馬上生效，無需重新打包。

---

## 結果頁／列印中（Vue .env）

在 **web-vue/app** 的 `.env` 或 `.env.example` 可設定：

| 變數 | 說明 | 預設值 |
|------|------|--------|
| `VITE_RESULT_AUTO_PRINT_SEC` | 結果頁幾秒沒按列印就自動列印 | 60 |
| `VITE_PRINTING_SHOW_SEC` | 列印中畫面顯示幾秒後回待機並還原 | 20 |

修改後需重新 **`npx vite build`** 才會生效。

### DNP 印完會通知 C# 嗎？

一般 **DNP Hot Folder** 不會在印完後主動通知 C#：我們把檔案丟進 Prints 資料夾後，由 Hot Folder.exe 送給印表機，程式端沒有「印好了」的回呼。  
因此目前做法是：列印中畫面顯示 **VITE_PRINTING_SHOW_SEC** 秒（預設 20 秒）後，自動回待機並 **resetSession**（清掉已拍照片），讓下一位可以重新拍照。  
若你的 DNP 有提供「印完回報」（例如寫檔、API、事件），可再在 C# 接該訊號並用 `PostWebMessageAsString` 通知 Vue 提早回待機。

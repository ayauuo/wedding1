# 將外部 GitHub Vue 專案引用為 web-vue（Git Submodule）

`web-vue` 設計為引用你放在**其他 GitHub 倉庫**的 Vue 前端專案，使用 **Git Submodule** 即可。

## 步驟一：加入 Submodule（只需做一次）

本專案已引用 **CJ0423/web-vue** 為 submodule，對應路徑：`source/repos/PhotoBoothWin/PhotoBoothWin/web-vue`，URL：`git@github.com:CJ0423/web-vue.git`。

若需在其他環境重新加入 submodule，在 **photobooth-kiosk 倉庫根目錄**執行：

```bash
git submodule add git@github.com:CJ0423/web-vue.git source/repos/PhotoBoothWin/PhotoBoothWin/web-vue
```

- 若 `web-vue` 資料夾已存在且為空，請先刪除再執行上述指令：
  ```bash
  rmdir source\repos\PhotoBoothWin\PhotoBoothWin\web-vue
  ```

執行後會：

- 在 `source/repos/PhotoBoothWin/PhotoBoothWin/web-vue` 建立資料夾並 clone 你的 Vue 倉庫。
- 在專案根目錄產生或更新 `.gitmodules`，記錄 submodule 的 URL 與路徑。

接著提交變更：

```bash
git add .gitmodules source/repos/PhotoBoothWin/PhotoBoothWin/web-vue
git commit -m "Add web-vue as submodule (Vue frontend from GitHub)"
```

## 步驟二：別人 clone 本專案時如何取得 Vue 程式碼

第一次 clone 本專案後，要一併取得 submodule 內容：

```bash
git clone https://github.com/你的帳號/photobooth-kiosk.git
cd photobooth-kiosk
git submodule update --init --recursive
```

之後若你更新了 submodule 的指向（例如在 Vue 倉庫推了新 commit），其他人要同步：

```bash
git submodule update --remote --merge
```

## 步驟三：在 web-vue 裡開發與 build（**一定要先做，WPF 才會讀到 Vue 畫面**）

WPF 讀取的是 **exe 旁的 `web` 資料夾**，內容來自專案裡的 `PhotoBoothWin/PhotoBoothWin/web/`。  
所以**必須先**在 Vue 專案裡 build，把輸出寫入 `web/`，**再**在 Visual Studio 建置／執行 WPF，畫面上才會是 **web-vue** 的內容；否則會是舊的或空的。

Vue 專案實際在 **web-vue/app/** 目錄下：

```bash
cd source/repos/PhotoBoothWin/PhotoBoothWin/web-vue/app
npm install
npx vite build
```

- 若 `npm run build` 因 TypeScript type-check 失敗，可改跑 **`npx vite build`**（只建置、不跑 type-check），一樣會輸出到 `web/`。
- 輸出目錄已在 `vite.config.js` / `vite.config.ts` 設為 `outDir: '../../web'`、`base: './'`，建置結果會寫入 WPF 專案用的 `web/`。

**建議流程**：改完 Vue → 在 `web-vue/app` 執行 `npx vite build` → 在 Visual Studio 建置並執行 WPF。

## 常用 Submodule 指令

| 情境 | 指令 |
|------|------|
| 更新 submodule 到遠端最新 commit | `git submodule update --remote source/repos/PhotoBoothWin/PhotoBoothWin/web-vue` |
| 進入 web-vue 拉最新 | `cd source/repos/PhotoBoothWin/PhotoBoothWin/web-vue` 後執行 `git pull` |
| 本專案要記錄「使用 Vue 倉庫的某個新 commit」 | 在專案根目錄 `git add source/repos/PhotoBoothWin/PhotoBoothWin/web-vue` 再 commit |

這樣就可以直接引用你其他地方的 GitHub Vue 專案，並在 photobooth-kiosk 裡用 `web-vue` 當作前端來源。

---

## 列印與紀錄相關 ENV（Vue .env）

在 **web-vue/app** 的 `.env` 或 `.env.example` 可設定：

| 變數 | 說明 | 範例 |
|------|------|------|
| `VITE_SKIP_PRINT` | 測試時不實際列印：設為 `1` 或 `true` 時只顯示列印中畫面、不送 DNP、不寫入列印紀錄 CSV | `0`（正式）、`1`（測試） |
| `VITE_RECEIPT_AMOUNT` | 每筆列印紀錄的收款金額（固定值，寫入 CSV 用） | `100` |
| `VITE_RESULT_AUTO_PRINT_SEC` | 結果頁幾秒沒按列印就自動列印（預設 60） | `60` |
| `VITE_PRINTING_SHOW_SEC` | 列印中畫面顯示幾秒後回待機（預設 20） | `15` 或 `20` |

## 列印紀錄 CSV（C# 寫入）

每次**實際列印**（按鈕或 60 秒自動）時，Vue 會呼叫 C# 的 `log_print_record`，由 C# 將一筆紀錄寫入 CSV：

- **API**：Vue 透過 `callHost('log_print_record', { templateName, printTime, amount })` 呼叫，C# 寫入 CSV。
- **欄位**：選擇的版型、列印時間、收款金額（消費金額）。
- **路徑**：預設為程式目錄下的 **`report/print_log.csv`**（打包後 exe 旁會有 `report` 資料夾）；可透過環境變數 `PHOTOBOOTH_PRINT_LOG_PATH` 或程式目錄的 `print_log_path.txt`（第一行）覆寫。
- **收款金額**：由 Vue 從 `VITE_RECEIPT_AMOUNT` 讀取後傳給 C#，因此可在 .env 固定設定。

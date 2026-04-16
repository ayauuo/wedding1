# npm 指令故障排除（Windows）

Node.js 已安裝但 `npm` 無法執行時，可依下列項目檢查。

## 1. 錯誤：「無法載入檔案 npm.ps1，因為這個系統上已停用指令碼執行」

**原因**：PowerShell 執行原則不允許執行腳本，而 `npm` 在 Windows 上會呼叫 `npm.ps1`。

**解法**：在 **PowerShell（以系統管理員身分開啟）** 執行：

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

若只想對目前使用者放行，不需管理員權限也可用：

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

執行後關閉並重新開啟終端機，再試一次 `npm -v`。

---

## 2. 錯誤：「npm 不是內部或外部命令」或「'npm' 不是可辨識的 CMDlet」

**原因**：Node.js 安裝目錄不在 PATH，或終端機未重新載入環境變數。

**解法**：

1. 確認 Node 安裝路徑（多數為）：`C:\Program Files\nodejs`
2. 確認該路徑在 **系統環境變數** 的 `Path` 中：  
   Win + R → 輸入 `sysdm.cpl` → 進階 → 環境變數 → 系統變數「Path」→ 編輯 → 新增 `C:\Program Files\nodejs`
3. **關閉所有終端機、VS Code / Cursor**，再重新開啟，然後執行 `npm -v`。

---

## 3. 改用 npm.cmd（PowerShell 擋腳本時）

若暫時不想改執行原則，可在 **命令提示字元 (CMD)** 使用：

```cmd
npm.cmd -v
npm.cmd install
```

或把 Node 目錄下的 `npm.cmd` 路徑加入 PATH，並在需要時直接打 `npm.cmd`。

---

## 4. 本專案建議的終端機

- 在 **Cursor / VS Code** 內建終端機執行 `npm` 時，若出現「已停用指令碼執行」，請先完成 **步驟 1** 的 `Set-ExecutionPolicy`。
- 或改用 **命令提示字元 (CMD)** 開啟專案目錄後執行 `npm`（會使用 `npm.cmd`）。

---

## 5. 快速檢查指令

在 PowerShell 或 CMD 依序執行：

```powershell
node -v
npm -v
```

若 `node -v` 有版本號但 `npm -v` 失敗，多半是 **步驟 1（執行原則）** 或 **步驟 3（改用 npm.cmd）** 可解決。

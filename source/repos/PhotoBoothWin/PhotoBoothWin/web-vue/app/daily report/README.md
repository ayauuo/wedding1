# daily report

此資料夾用於存放**非測試模式**下的使用記錄 CSV。

## 主機端實作

當前端呼叫 `callHost('append_usage_log', { folder, time, templateId, projectName })` 時，請在此資料夾建立或追加 CSV 檔。

建議格式：

- **檔名**：例如 `usage.csv` 或依日期 `YYYY-MM-DD.csv`
- **表頭**：`time,template_id,project_name`
- **一列一筆**：`"2025-01-30T12:34:56.789Z","bk01","拍貼機"`

範例（CSV 內容）：

```csv
time,template_id,project_name
"2025-01-30T12:34:56.789Z","bk01","拍貼機"
"2025-01-30T12:35:10.123Z","bk02","拍貼機"
```

前端傳入的 `folder` 為 `daily report`，請依此建立或寫入本資料夾內的檔案。  
`*.csv` 已加入 .gitignore，不會被納入版控。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using PhotoBoothWin.Models;

namespace PhotoBoothWin.Services
{
    /// <summary>
    /// 本機 SQLite：細表（拍貼機_4格窗細表）＋列印紀錄（日總表／核銷表來源）。「上傳雲端」從此讀取；「清理資料庫」可一併清除。
    /// 預設 DB 路徑：程式目錄 report/photobooth_detail.db；可由 photo_detail_db_path.txt 覆寫。
    /// </summary>
    public static class PhotoDetailStore
    {
        private const string ReportFolderName = "report";
        private const string DefaultDbFileName = "photobooth_detail.db";

        private static string GetDbPath()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "photo_detail_db_path.txt");
                if (File.Exists(configPath))
                {
                    var line = File.ReadAllText(configPath).Trim();
                    if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
                }
            }
            catch { /* 沿用預設 */ }

            var reportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ReportFolderName);
            Directory.CreateDirectory(reportDir);
            return Path.Combine(reportDir, DefaultDbFileName);
        }

        private static void EnsureTable(SqliteConnection conn)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS PhotoDetail (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Date TEXT NOT NULL,
    Time TEXT NOT NULL,
    MachineName TEXT NOT NULL,
    FileName TEXT NOT NULL,
    LayoutType TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UploadedAt TEXT NULL,
    IsTest INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS PrintRecord (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Date TEXT NOT NULL,
    Time TEXT NOT NULL,
    ProjectName TEXT NOT NULL,
    MachineName TEXT NOT NULL,
    Amount INTEGER NOT NULL,
    TemplateName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UploadedAt TEXT NULL,
    Copies INTEGER NOT NULL DEFAULT 1,
    IsTest INTEGER NOT NULL DEFAULT 0
);
";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            EnsurePhotoDetailUploadedAtColumn(conn);
            EnsurePhotoDetailIsTestColumn(conn);
            EnsurePrintRecordUploadedAtColumn(conn);
            EnsurePrintRecordCopiesColumn(conn);
            EnsurePrintRecordIsTestColumn(conn);
            EnsureIndexes(conn);
        }

        private static void EnsurePrintRecordCopiesColumn(SqliteConnection conn)
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE PrintRecord ADD COLUMN Copies INTEGER NOT NULL DEFAULT 1";
                alter.ExecuteNonQuery();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message?.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) == true)
            { /* 欄位已存在 */ }
        }

        private static void EnsurePrintRecordIsTestColumn(SqliteConnection conn)
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE PrintRecord ADD COLUMN IsTest INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message?.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) == true)
            { /* 欄位已存在 */ }
        }

        /// <summary>既有 DB 可能沒有 UploadedAt，補上欄位。</summary>
        private static void EnsurePhotoDetailUploadedAtColumn(SqliteConnection conn)
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE PhotoDetail ADD COLUMN UploadedAt TEXT NULL";
                alter.ExecuteNonQuery();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message?.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) == true)
            {
                /* 欄位已存在 */
            }
        }

        private static void EnsurePhotoDetailIsTestColumn(SqliteConnection conn)
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE PhotoDetail ADD COLUMN IsTest INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message?.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) == true)
            { /* 欄位已存在 */ }
        }

        /// <summary>既有 DB 可能沒有 UploadedAt，補上欄位。</summary>
        private static void EnsurePrintRecordUploadedAtColumn(SqliteConnection conn)
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE PrintRecord ADD COLUMN UploadedAt TEXT NULL";
                alter.ExecuteNonQuery();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message?.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) == true)
            {
                /* 欄位已存在 */
            }
        }

        private static void EnsureIndexes(SqliteConnection conn)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_PhotoDetail_UploadedAt ON PhotoDetail(UploadedAt)";
                cmd.ExecuteNonQuery();
            }
            catch { /* 忽略索引建立失敗 */ }

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_PrintRecord_Date ON PrintRecord(Date)";
                cmd.ExecuteNonQuery();
            }
            catch { /* 忽略索引建立失敗 */ }

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_PrintRecord_UploadedAt ON PrintRecord(UploadedAt)";
                cmd.ExecuteNonQuery();
            }
            catch { /* 忽略索引建立失敗 */ }
        }

        /// <summary>寫入一筆細表紀錄，尚未上傳（UploadedAt 為 NULL）。isTest 為 true 時自動上傳會略過此筆。</summary>
        public static string? Insert(PhotoDetail detail, bool isTest = false)
        {
            try
            {
                var path = GetDbPath();
                // #region agent log
                try
                {
                    var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                    var line = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        location = "PhotoDetailStore.cs:Insert:entry",
                        message = "Insert PhotoDetail",
                        data = new
                        {
                            dbPath = path,
                            date = detail.Date ?? "",
                            time = detail.Time ?? "",
                            fileNameLen = (detail.FileName ?? "").Length,
                            layoutType = detail.LayoutType ?? "",
                            machineNameLen = (detail.MachineName ?? "").Length
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        sessionId = "debug-session",
                        runId = "run1",
                        hypothesisId = "H3"
                    }) + "\n";
                    File.AppendAllText(logPath, line);
                }
                catch { }
                // #endregion
                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                EnsureTable(conn);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO PhotoDetail (Date, Time, MachineName, FileName, LayoutType, CreatedAt, IsTest)
VALUES (@date, @time, @machineName, @fileName, @layoutType, @createdAt, @isTest);
";
                cmd.Parameters.AddWithValue("@date", detail.Date ?? "");
                cmd.Parameters.AddWithValue("@time", detail.Time ?? "");
                cmd.Parameters.AddWithValue("@machineName", detail.MachineName ?? "");
                cmd.Parameters.AddWithValue("@fileName", detail.FileName ?? "");
                cmd.Parameters.AddWithValue("@layoutType", detail.LayoutType ?? "");
                cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@isTest", isTest ? 1 : 0);
                cmd.ExecuteNonQuery();
                return null;
            }
            catch (Exception ex)
            {
                // #region agent log
                try
                {
                    var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                    var line = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        location = "PhotoDetailStore.cs:Insert:catch",
                        message = "Insert PhotoDetail failed",
                        data = new { error = ex.Message },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        sessionId = "debug-session",
                        runId = "run1",
                        hypothesisId = "H4"
                    }) + "\n";
                    File.AppendAllText(logPath, line);
                }
                catch { }
                // #endregion
                return ex.Message;
            }
        }

        /// <summary>取得所有尚未上傳的細表紀錄（UploadedAt IS NULL），依 Id 排序。</summary>
        public static List<(long Id, PhotoDetail Detail)> GetUnuploaded()
        {
            var list = new List<(long Id, PhotoDetail Detail)>();
            try
            {
                var path = GetDbPath();
                if (!File.Exists(path)) return list;

                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Date, Time, MachineName, FileName, LayoutType FROM PhotoDetail WHERE UploadedAt IS NULL AND (IsTest = 0 OR IsTest IS NULL) ORDER BY Id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var id = r.GetInt64(0);
                    var d = new PhotoDetail
                    {
                        Date = r.GetString(1),
                        Time = r.GetString(2),
                        MachineName = r.GetString(3),
                        FileName = r.GetString(4),
                        LayoutType = r.GetString(5)
                    };
                    list.Add((id, d));
                }
            }
            catch { /* 回傳空 */ }

            return list;
        }

        /// <summary>取得指定日期且尚未上傳的細表紀錄（Date = 指定日且 UploadedAt IS NULL），依 Id 排序。</summary>
        public static List<(long Id, PhotoDetail Detail)> GetUnuploadedByDate(DateTime date)
        {
            var list = new List<(long Id, PhotoDetail Detail)>();
            var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            try
            {
                var path = GetDbPath();
                if (!File.Exists(path)) return list;

                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Date, Time, MachineName, FileName, LayoutType FROM PhotoDetail WHERE Date = @dateStr AND UploadedAt IS NULL ORDER BY Id";
                cmd.Parameters.AddWithValue("@dateStr", dateStr);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var id = r.GetInt64(0);
                    var d = new PhotoDetail
                    {
                        Date = r.GetString(1),
                        Time = r.GetString(2),
                        MachineName = r.GetString(3),
                        FileName = r.GetString(4),
                        LayoutType = r.GetString(5)
                    };
                    list.Add((id, d));
                }
            }
            catch { /* 回傳空 */ }

            return list;
        }

        /// <summary>將指定 Id 標記為已上傳（UploadedAt = 現在）。</summary>
        public static void MarkAsUploaded(long id)
        {
            try
            {
                var path = GetDbPath();
                if (!File.Exists(path)) return;

                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE PhotoDetail SET UploadedAt = @t WHERE Id = @id";
                cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            catch { /* 忽略 */ }
        }

        /// <summary>寫入一筆列印紀錄（日總表／核銷表來源）。成功回傳 null，失敗回傳錯誤訊息。</summary>
        public static string? InsertPrintRecord(string templateName, string printTime, string amountStr, string projectName, string machineName, int copies = 1, bool isTest = false)
        {
            try
            {
                var path = GetDbPath();
                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                EnsureTable(conn);

                var dateStr = "";
                var timeStr = "";
                if (!string.IsNullOrWhiteSpace(printTime) && DateTime.TryParse(printTime, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    if (dt.Kind == DateTimeKind.Utc) dt = dt.ToLocalTime();
                    dateStr = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    timeStr = dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                }
                else
                {
                    var n = DateTime.Now;
                    dateStr = n.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    timeStr = n.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                }

                var amount = 0;
                if (!string.IsNullOrWhiteSpace(amountStr))
                    _ = decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) && (amount = (int)Math.Round(amt)) >= 0;

                copies = Math.Clamp(copies, 1, 99);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO PrintRecord (Date, Time, ProjectName, MachineName, Amount, TemplateName, CreatedAt, Copies, IsTest)
VALUES (@date, @time, @projectName, @machineName, @amount, @templateName, @createdAt, @copies, @isTest);
";
                cmd.Parameters.AddWithValue("@date", dateStr);
                cmd.Parameters.AddWithValue("@time", timeStr);
                cmd.Parameters.AddWithValue("@projectName", projectName ?? "");
                cmd.Parameters.AddWithValue("@machineName", machineName ?? "");
                cmd.Parameters.AddWithValue("@amount", amount);
                cmd.Parameters.AddWithValue("@templateName", templateName ?? "");
                cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@copies", copies);
                cmd.Parameters.AddWithValue("@isTest", isTest ? 1 : 0);
                cmd.ExecuteNonQuery();
                return null;
            }
            catch (Exception ex)
            {
                // #region agent log
                try
                {
                    var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                    var line = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        location = "PhotoDetailStore.cs:InsertPrintRecord:catch",
                        message = "Insert PrintRecord failed",
                        data = new { error = ex.Message },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        sessionId = "debug-session",
                        runId = "run1",
                        hypothesisId = "H4"
                    }) + "\n";
                    File.AppendAllText(logPath, line);
                }
                catch { }
                // #endregion
                return ex.Message;
            }
        }

        /// <summary>從 SQLite 取得指定日期的彙總（日總表／核銷表用）。</summary>
        public static List<SummaryReport> GetTodaySummaryReportsFromDb(DateTime date)
        {
            var list = new List<SummaryReport>();
            var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            try
            {
                var path = GetDbPath();
                if (!File.Exists(path)) return list;

                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT Date, ProjectName, MachineName, COUNT(*) AS Cnt, SUM(Amount) AS Total, (SUM(Amount) / COUNT(*)) AS AvgAmount
FROM PrintRecord WHERE Date = @dateStr AND UploadedAt IS NULL AND IsTest = 0
GROUP BY Date, ProjectName, MachineName
ORDER BY Date, ProjectName, MachineName;
";
                cmd.Parameters.AddWithValue("@dateStr", dateStr);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var total = r.GetInt32(4);
                    var cnt = r.GetInt32(3);
                    var avg = r.IsDBNull(5) ? 0 : r.GetInt32(5);
                    list.Add(new SummaryReport
                    {
                        Date = r.GetString(0),
                        ProjectName = r.GetString(1),
                        MachineName = r.GetString(2),
                        DailySalesCount = cnt,
                        DailyRevenue = total,
                        UnitPrice = avg
                    });
                }
            }
            catch { /* 回傳空 */ }

            return list;
        }

        /// <summary>取得有「未上傳」PrintRecord 的日期列表（供一次補傳多日）。</summary>
        public static List<DateTime> GetUnuploadedPrintRecordDates()
        {
            var list = new List<DateTime>();
            try
            {
                var path = GetDbPath();
                if (!File.Exists(path)) return list;

                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT Date FROM PrintRecord WHERE UploadedAt IS NULL AND IsTest = 0 ORDER BY Date";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var s = r.GetString(0);
                    if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                        list.Add(dt.Date);
                }
            }
            catch { /* 回傳空 */ }

            return list;
        }

        /// <summary>將指定日期的 PrintRecord 全部標記為已上傳。</summary>
        public static void MarkPrintRecordsUploadedByDate(DateTime date)
        {
            var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            try
            {
                var path = GetDbPath();
                if (!File.Exists(path)) return;

                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE PrintRecord SET UploadedAt = @t WHERE Date = @dateStr AND IsTest = 0";
                cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@dateStr", dateStr);
                cmd.ExecuteNonQuery();
            }
            catch { /* 忽略 */ }
        }

        /// <summary>取得目前使用的 DB 路徑（供日誌或前端顯示）。</summary>
        public static string GetDbPathForDisplay()
        {
            return GetDbPath();
        }

        /// <summary>塞入假列印紀錄，供觀看資料庫測試用。回傳新增筆數。</summary>
        public static int SeedFakePrintRecords()
        {
            var machineName = Environment.MachineName ?? "TestMachine";
            var projectName = "測試專案";
            var templates = new[] { "bk01", "bk02", "bk03" };
            var rand = new Random();
            var inserted = 0;
            try
            {
                for (var dayOffset = -5; dayOffset <= 0; dayOffset++)
                {
                    var d = DateTime.Today.AddDays(dayOffset);
                    var dateStr = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var count = 2 + rand.Next(0, 4);
                    for (var i = 0; i < count; i++)
                    {
                        var timeStr = $"{rand.Next(9, 18):D2}:{rand.Next(0, 60):D2}:{rand.Next(0, 60):D2}";
                        var amount = new[] { 100, 100, 200, 200 }[rand.Next(0, 4)];
                        var copies = rand.Next(1, 4);
                        var isTest = rand.Next(0, 5) == 0;
                        var templateName = templates[rand.Next(0, templates.Length)];
                        var err = InsertPrintRecord(templateName, $"{dateStr}T{timeStr}", amount.ToString(), projectName, machineName, copies, isTest);
                        if (err == null) inserted++;
                    }
                }
            }
            catch { /* 忽略 */ }
            return inserted;
        }

        /// <summary>觀看資料庫用：依日期與範圍類型查詢列印紀錄，回傳明細與總列印張數、測試總張數。rangeType: day | week | month。</summary>
        public static (List<PrintRecordViewRow> Rows, int TotalPrintSheets, int TotalTestSheets) GetPrintRecordsForView(DateTime date, string rangeType)
        {
            var rows = new List<PrintRecordViewRow>();
            int totalPrintSheets = 0;
            int totalTestSheets = 0;
            try
            {
                var path = GetDbPath();
                if (!File.Exists(path)) return (rows, 0, 0);

                string dateFilter;
                string dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (rangeType == "week")
                    dateFilter = "Date >= @start AND Date <= @end";
                else if (rangeType == "month")
                    dateFilter = "Date LIKE @monthPrefix";
                else
                {
                    dateFilter = "Date = @dateStr";
                }

                var sql = $@"
SELECT Id, Date, Time, ProjectName, MachineName, Amount, TemplateName, Copies, IsTest
FROM PrintRecord
WHERE {dateFilter}
ORDER BY Date, Time, Id";
                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@dateStr", dateStr);
                if (rangeType == "week")
                {
                    var start = date;
                    while (start.DayOfWeek != DayOfWeek.Monday) start = start.AddDays(-1);
                    var end = start.AddDays(6);
                    cmd.Parameters.AddWithValue("@start", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("@end", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                if (rangeType == "month")
                    cmd.Parameters.AddWithValue("@monthPrefix", date.ToString("yyyy-MM", CultureInfo.InvariantCulture) + "%");

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var copies = r.IsDBNull(7) ? 1 : r.GetInt32(7);
                    var isTest = !r.IsDBNull(8) && r.GetInt32(8) != 0;
                    totalPrintSheets += copies;
                    if (isTest) totalTestSheets += copies;
                    rows.Add(new PrintRecordViewRow
                    {
                        Id = r.GetInt64(0),
                        Date = r.GetString(1),
                        Time = r.GetString(2),
                        ProjectName = r.GetString(3),
                        MachineName = r.GetString(4),
                        Amount = r.GetInt32(5),
                        TemplateName = r.GetString(6),
                        Copies = copies,
                        IsTest = isTest
                    });
                }
            }
            catch { /* 回傳空 */ }
            return (rows, totalPrintSheets, totalTestSheets);
        }

        /// <summary>刪除資料庫內所有資料（PhotoDetail ＋ PrintRecord）。成功回傳 (null, 細表刪除筆數, 列印紀錄刪除筆數, 路徑)。</summary>
        public static (string? Error, int DetailDeleted, int PrintRecordDeleted, string DbPath) ClearAll()
        {
            var path = GetDbPath();
            try
            {
                if (!File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine($"[SQLite] ClearAll: 檔案不存在，路徑={path}");
                    return (null, 0, 0, path);
                }

                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                int detailCount = 0;
                int printCount = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM PhotoDetail";
                    detailCount = Convert.ToInt32(cmd.ExecuteScalar());
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM PrintRecord";
                    printCount = Convert.ToInt32(cmd.ExecuteScalar());
                }
                using (var cmd1 = conn.CreateCommand())
                {
                    cmd1.CommandText = "DELETE FROM PhotoDetail";
                    cmd1.ExecuteNonQuery();
                }
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = "DELETE FROM PrintRecord";
                    cmd2.ExecuteNonQuery();
                }
                System.Diagnostics.Debug.WriteLine($"[SQLite] ClearAll: 細表 {detailCount} 筆、列印紀錄 {printCount} 筆已刪除，路徑={path}");
                return (null, detailCount, printCount, path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SQLite] ClearAll 失敗: {ex.Message}，路徑={path}");
                return (ex.Message, 0, 0, path);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PhotoBoothWin.Models;

namespace PhotoBoothWin.Services
{
    /// <summary>
    /// 列印紀錄 CSV：選擇的版型、列印時間、收款金額、專案名稱、機器名稱。
    /// 預設寫入程式目錄下 report/print_log.csv；路徑可由環境變數 PHOTOBOOTH_PRINT_LOG_PATH 或程式目錄 print_log_path.txt 覆寫。
    /// </summary>
    public static class PrintLogHelper
    {
        private const string ReportFolderName = "report";
        private const string DefaultFileName = "print_log.csv";

        private static string GetLogPath()
        {
            var fromEnv = Environment.GetEnvironmentVariable("PHOTOBOOTH_PRINT_LOG_PATH");
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return fromEnv.Trim();

            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "print_log_path.txt");
                if (File.Exists(configPath))
                {
                    var line = File.ReadAllText(configPath).Trim();
                    if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
                }
            }
            catch { /* 沿用預設 */ }

            var reportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ReportFolderName);
            return Path.Combine(reportDir, DefaultFileName);
        }

        /// <summary>CSV 欄位逸出：若含逗號或雙引號則包雙引號並將內部雙引號加倍。</summary>
        private static string EscapeCsvField(string value)
        {
            if (value == null) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>追加一筆列印紀錄（含專案名稱、機器名稱）。若檔案不存在會先寫入標題列。回傳錯誤訊息，成功則 null。</summary>
        public static string? AppendRecord(string templateName, string printTime, string amount, string projectName = "", string machineName = "")
        {
            var path = GetLogPath();
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var needHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
                var line = $"{EscapeCsvField(templateName)},{EscapeCsvField(printTime)},{EscapeCsvField(amount)},{EscapeCsvField(projectName)},{EscapeCsvField(machineName)}{Environment.NewLine}";

                if (needHeader)
                {
                    var header = $"選擇的版型,列印時間,收款金額,專案名稱,機器名稱{Environment.NewLine}";
                    File.WriteAllText(path, header + line, new UTF8Encoding(true));
                }
                else
                {
                    File.AppendAllText(path, line, new UTF8Encoding(true));
                }

                Debug.WriteLine($"[列印紀錄] 已寫入: {Path.GetFullPath(path)}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[列印紀錄] 寫入失敗 path={Path.GetFullPath(path)} error={ex.Message}");
                return ex.Message;
            }
        }

        /// <summary>讀取 CSV 並彙總指定日期的紀錄，回傳 SummaryReport 列表（依 date, projectName, machineName 分組）。</summary>
        public static List<SummaryReport> GetTodaySummaryReports(DateTime date)
        {
            var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var path = GetLogPath();
            var list = new List<SummaryReport>();
            // #region agent log
            try
            {
                var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                var exists = File.Exists(path);
                var lineCount = 0;
                if (exists) try { lineCount = File.ReadAllLines(path, new UTF8Encoding(true)).Length; } catch { }
                var line0 = System.Text.Json.JsonSerializer.Serialize(new { location = "PrintLogHelper.cs:GetTodaySummaryReports:entry", message = "GetTodaySummaryReports", data = new { dateStr, path, pathFull = Path.GetFullPath(path), fileExists = exists, lineCount }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H5" }) + "\n";
                File.AppendAllText(logPath, line0);
            }
            catch { }
            // #endregion
            if (!File.Exists(path)) return list;

            var groups = new Dictionary<string, (int count, int revenue, int unitPrice)>();

            try
            {
                var lines = File.ReadAllLines(path, new UTF8Encoding(true));
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i == 0 && lines[i].StartsWith("選擇的版型", StringComparison.Ordinal)) continue;
                    var parts = ParseCsvLine(lines[i]);
                    if (parts.Count < 3) continue;
                    var printTime = parts[1];
                    if (string.IsNullOrWhiteSpace(printTime)) continue;
                    var recordDate = DateTime.TryParse(printTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                        ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : "";
                    if (recordDate != dateStr) continue;

                    var amountStr = parts.Count > 2 ? parts[2] : "0";
                    decimal amt = 0;
                    decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out amt);
                    var amount = (int)Math.Round(amt);

                    var projectName = parts.Count > 3 ? parts[3] : "";
                    var machineName = parts.Count > 4 ? parts[4] : Environment.MachineName ?? "";
                    var key = $"{recordDate}\t{projectName}\t{machineName}";
                    if (!groups.TryGetValue(key, out var g))
                        groups[key] = (1, amount, amount);
                    else
                        groups[key] = (g.count + 1, g.revenue + amount, g.unitPrice);
                }

                foreach (var kv in groups)
                {
                    var keyParts = kv.Key.Split('\t');
                    list.Add(new SummaryReport
                    {
                        Date = keyParts.Length > 0 ? keyParts[0] : dateStr,
                        ProjectName = keyParts.Length > 1 ? keyParts[1] : "",
                        MachineName = keyParts.Length > 2 ? keyParts[2] : "",
                        UnitPrice = kv.Value.unitPrice,
                        DailySalesCount = kv.Value.count,
                        DailyRevenue = kv.Value.revenue
                    });
                }
                // #region agent log
                try
                {
                    var logPath2 = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                    var line2 = System.Text.Json.JsonSerializer.Serialize(new { location = "PrintLogHelper.cs:GetTodaySummaryReports:after_aggregate", message = "aggregate result", data = new { dateStr, groupsCount = groups.Count, listCount = list.Count }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H2" }) + "\n";
                    File.AppendAllText(logPath2, line2);
                }
                catch { }
                // #endregion
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[列印紀錄] 讀取彙總失敗 path={path} error={ex.Message}");
                // #region agent log
                try
                {
                    var logPath3 = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                    var line3 = System.Text.Json.JsonSerializer.Serialize(new { location = "PrintLogHelper.cs:GetTodaySummaryReports:catch", message = "GetTodaySummaryReports exception", data = new { error = ex.Message }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H5" }) + "\n";
                    File.AppendAllText(logPath3, line3);
                }
                catch { }
                // #endregion
            }

            return list.OrderBy(x => x.Date).ThenBy(x => x.ProjectName).ThenBy(x => x.MachineName).ToList();
        }

        private static List<string> ParseCsvLine(string line)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if ((c == ',' && !inQuotes) || c == '\r' || c == '\n')
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                    if (c == '\r' || c == '\n') break;
                }
                else
                    sb.Append(c);
            }
            list.Add(sb.ToString());
            return list;
        }
    }
}

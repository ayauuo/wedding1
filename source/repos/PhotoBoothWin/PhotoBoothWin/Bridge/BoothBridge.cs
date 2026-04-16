using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using PhotoBoothWin.Models;
using PhotoBoothWin.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PhotoBoothWin.Bridge
{
    public class BoothBridge
    {
        /// <summary>Vue 呼叫 start_liveview 後為 true，MainWindow 會把 EDSDK Live View 幀推送到 WebView。</summary>
        public static bool LiveViewPushToWeb { get; set; }
        private static int _pendingStartLiveView;

        /// <summary>Vue 呼叫 open_wpf_shoot 時由 MainWindow 設定，用來切換到 WPF 拍照頁（10 秒倒數 + EDSDK）。</summary>
        public static Action? OpenWpfShootRequested { get; set; }
        /// <summary>WPF 拍照流程中「返回主畫面」時由 MainWindow 設定。</summary>
        public static Action? ReturnToWebViewRequested { get; set; }
        /// <summary>目前是否從 Vue 切換過來的 WPF 拍照流程（返回時回 Vue）。</summary>
        public static bool IsWpfShootEmbedded { get; set; }

        /// <summary>WPF 拍照流程中「下一步」在濾鏡模式按下時，由 MainWindow 設定：回到 WebView 並觸發 Vue 合成（load_captures → save_image/upload_file）。</summary>
        public static Action? ReturnToWebAndStartSynthesisRequested { get; set; }

        /// <summary>將 WPF 版型代碼（A/B/C）對應為 Vue 版型 id（bk01/bk02/bk03），供合成時使用。</summary>
        public static string GetVueTemplateIdForSynthesis()
        {
            var wpfId = PhotoBoothWin.BoothStore.Current.TemplateId ?? "";
            return wpfId switch { "A" => "bk01", "B" => "bk02", "C" => "bk03", _ => "bk01" };
        }

        private static readonly HttpClient _http = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(120), // 大圖上傳較久，避免逾時中斷
        };

        /// <summary>上傳 API 網址，與 Vue 的 VITE_UPLOAD_URL 對應。可於程式目錄放 upload_url.txt 覆寫。</summary>
        public static string UploadUrl { get; set; } = "https://www.guoli-tw.com/upload_model/api/upload.php";

        /// <summary>Google Apps Script 網址，用於上傳與照片紀錄至 Google 試算表。</summary>
        public static string GoogleScriptUrl { get; set; } = "https://script.google.com/macros/s/AKfycbyBBReW0UpFCy0lEiCBTBNrAneIf4h7zuC4jxWKHEfiO6OPS66CG58XU87gu6cBRI0kfw/exec";

        /// <summary>EDSDK 拍照儲存資料夾，與 MainWindow 的 photos 虛擬主機對應，前端用 https://photos/檔名 載入。</summary>
        public static string CaptureOutputDirectory { get; set; } = @"C:\test";

        private static MediaPlayer? _countdownPlayer;

        public string? Handle(string json) => HandleAsync(json).GetAwaiter().GetResult();

        public async Task<string?> HandleAsync(string json)
        {
            RpcRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<RpcRequest>(json);
                if (req == null) return null;
            }
            catch
            {
                return null;
            }

            try
            {
                switch (req.cmd)
                {
                    case "print_hotfolder":
                        {
                            var sizeKey = req.data.GetProperty("sizeKey").GetString() ?? "4x6";
                            var filePath = req.data.GetProperty("filePath").GetString() ?? "";

                            int copies = 1;
                            if (req.data.TryGetProperty("copies", out var c) &&
                                c.ValueKind == System.Text.Json.JsonValueKind.Number)
                            {
                                copies = c.GetInt32();
                            }

                            copies = Math.Clamp(copies, 1, 5);

                            HotFolderPrinter.SendToHotFolder(filePath, sizeKey, copies);
                            return Ok(req.id, new { copies });
                        }

                    case "log_print_record":
                        {
                            // #region agent log
                            try
                            {
                                var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                                var line = System.Text.Json.JsonSerializer.Serialize(new
                                {
                                    location = "BoothBridge.cs:log_print_record:entry",
                                    message = "log_print_record received",
                                    data = new
                                    {
                                        templateName = req.data.TryGetProperty("templateName", out var t0) ? t0.GetString() ?? "" : "",
                                        printTime = req.data.TryGetProperty("printTime", out var p0) ? p0.GetString() ?? "" : "",
                                        amount = req.data.TryGetProperty("amount", out var a0) ? a0.GetString() ?? "" : "",
                                        projectName = req.data.TryGetProperty("projectName", out var pn0) ? pn0.GetString() ?? "" : "",
                                        machineName = req.data.TryGetProperty("machineName", out var mn0) ? mn0.GetString() ?? "" : "",
                                        fileNameLen = req.data.TryGetProperty("fileName", out var fn0) ? (fn0.GetString() ?? "").Length : 0,
                                        copies = req.data.TryGetProperty("copies", out var cp0) && cp0.ValueKind == System.Text.Json.JsonValueKind.Number ? cp0.GetInt32() : 0
                                    },
                                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    sessionId = "debug-session",
                                    runId = "run1",
                                    hypothesisId = "H2"
                                }) + "\n";
                                File.AppendAllText(logPath, line);
                            }
                            catch { }
                            // #endregion
                            var templateName = req.data.TryGetProperty("templateName", out var tn) ? tn.GetString() ?? "" : "";
                            var printTime = req.data.TryGetProperty("printTime", out var pt) ? pt.GetString() ?? "" : "";
                            var amountStr = req.data.TryGetProperty("amount", out var am) ? am.GetString() ?? "" : "";
                            var projectName = req.data.TryGetProperty("projectName", out var pn) ? pn.GetString() ?? "" : "";
                            var machineName = req.data.TryGetProperty("machineName", out var mn) ? mn.GetString() ?? "" : "";
                            if (string.IsNullOrWhiteSpace(machineName)) machineName = Environment.MachineName ?? "";
                            var fileName = req.data.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
                            int copies = 1;
                            if (req.data.TryGetProperty("copies", out var cp) && cp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                copies = Math.Clamp(cp.GetInt32(), 1, 99);
                            bool isTest = false;
                            if (req.data.TryGetProperty("isTest", out var it) && (it.ValueKind == System.Text.Json.JsonValueKind.True || it.ValueKind == System.Text.Json.JsonValueKind.Number && it.GetInt32() != 0))
                                isTest = true;
                            var err = PrintLogHelper.AppendRecord(templateName, printTime, amountStr, projectName, machineName);
                            if (err != null) return RespFail(req.id, err);
                            var errDb = PhotoDetailStore.InsertPrintRecord(templateName, printTime, amountStr, projectName, machineName, copies, isTest);
                            if (errDb != null)
                                System.Diagnostics.Debug.WriteLine($"[SQLite 列印紀錄] 寫入失敗: {errDb}");
                            if (!string.IsNullOrWhiteSpace(fileName))
                            {
                                var (datePart, timePart) = ParseDateTimeParts(printTime);
                                var photo = new PhotoBoothWin.Models.PhotoDetail
                                {
                                    Date = datePart,
                                    Time = timePart,
                                    MachineName = machineName,
                                    FileName = fileName.Trim(),
                                    LayoutType = templateName
                                };
                                var insertErr = PhotoDetailStore.Insert(photo, isTest);
                                if (insertErr != null)
                                    System.Diagnostics.Debug.WriteLine($"[細表 SQLite] 寫入失敗: {insertErr}");
                            }

                            return Ok(req.id, new { });
                        }

                    case "upload_to_google":
                        {
                            // #region agent log
                            try
                            {
                                var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                                var line0 = System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.cs:upload_to_google:entry", message = "upload_to_google invoked", data = new { }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H1" }) + "\n";
                                File.AppendAllText(logPath, line0);
                            }
                            catch { }
                            // #endregion
                            var dateStr = req.data.TryGetProperty("date", out var uds) ? uds.GetString() ?? "" : "";
                            var datesToUpload = new List<DateTime>();
                            if (!string.IsNullOrWhiteSpace(dateStr) && DateTime.TryParse(dateStr, out var uparsed))
                            {
                                datesToUpload.Add(uparsed.Date);
                            }
                            else
                            {
                                // 未指定日期：一次上傳所有未上傳的日期（多天沒連線後補傳）
                                datesToUpload = PhotoDetailStore.GetUnuploadedPrintRecordDates();
                                if (datesToUpload.Count == 0)
                                    datesToUpload.Add(DateTime.Today.AddDays(-1));
                            }

                            var totalSummaryRows = 0;
                            foreach (var date in datesToUpload)
                            {
                                var summaryList = PhotoDetailStore.GetTodaySummaryReportsFromDb(date);
                                if (summaryList.Count == 0) continue;
                                var err1 = await UploadDataAsync("日總表", summaryList).ConfigureAwait(false);
                                var err2 = await UploadDataAsync("拍貼機_4格窗核銷表", summaryList).ConfigureAwait(false);
                                if (err1 != null || err2 != null)
                                    return RespFail(req.id, $"日總表:{err1 ?? "ok"} 核銷表:{err2 ?? "ok"}");
                                PhotoDetailStore.MarkPrintRecordsUploadedByDate(date);
                                totalSummaryRows += summaryList.Count;
                            }

                            var detailUploaded = 0;
                            var unuploaded = PhotoDetailStore.GetUnuploaded();
                            foreach (var (id, detail) in unuploaded)
                            {
                                var errDetail = await UploadDataAsync("拍貼機_4格窗細表", detail).ConfigureAwait(false);
                                if (errDetail == null)
                                {
                                    PhotoDetailStore.MarkAsUploaded(id);
                                    detailUploaded++;
                                }
                                else
                                    System.Diagnostics.Debug.WriteLine($"[Google 細表] Id={id} 上傳失敗: {errDetail}");
                            }

                            return Ok(req.id, new { uploaded = true, summaryCount = totalSummaryRows, detailUploaded, datesUploaded = datesToUpload.Count });
                        }

                    case "test_google_sheet":
                        {
                            var url = req.data.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                            if (string.IsNullOrWhiteSpace(url)) url = GoogleScriptUrl;
                            var getResult = await TestGoogleSheetGetAsync(url).ConfigureAwait(false);
                            var postResult = await TestGoogleSheetPostAsync(url).ConfigureAwait(false);
                            return Ok(req.id, new { get = getResult, post = postResult });
                        }

                    case "make_test_image":
                        {
                            var outDir = CaptureOutputDirectory;
                            System.IO.Directory.CreateDirectory(outDir);
                            var path = System.IO.Path.Combine(outDir, "test.jpg");

                            TestImageMaker.Make(path); // 下面會給你這個類別
                            return Ok(req.id, new { filePath = path });
                        }

                    case "clear_captures":
                        {
                            CameraCaptureStore.Clear();
                            return Ok(req.id, new { });
                        }

                    case "clear_photo_detail_db":
                        {
                            System.Diagnostics.Debug.WriteLine("[SQLite] clear_photo_detail_db RPC 收到");
                            var (err, detailDeleted, printRecordDeleted, dbPath) = PhotoDetailStore.ClearAll();
                            if (err != null)
                                return RespFail(req.id, err);
                            return Ok(req.id, new { deletedCount = detailDeleted + printRecordDeleted, detailDeleted, printRecordDeleted, dbPath });
                        }

                    case "get_print_records":
                        {
                            var dateStr = req.data.TryGetProperty("date", out var ds) ? ds.GetString() ?? "" : "";
                            var rangeType = req.data.TryGetProperty("rangeType", out var rt) ? rt.GetString() ?? "day" : "day";
                            if (string.IsNullOrWhiteSpace(dateStr) || !DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parseDate))
                                parseDate = DateTime.Today;
                            if (rangeType != "week" && rangeType != "month") rangeType = "day";
                            var (rows, totalPrintSheets, totalTestSheets) = PhotoDetailStore.GetPrintRecordsForView(parseDate, rangeType);
                            return Ok(req.id, new { rows, totalPrintSheets, totalTestSheets });
                        }

                    case "seed_fake_data":
                        // 已關閉塞入假資料功能
                        return Ok(req.id, new { inserted = 0 });

                    case "load_captures":
                        {
                            // #region agent log
                            var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                            System.Diagnostics.Debug.WriteLine("[Bridge] 收到 load_captures 請求...");
                            try { File.AppendAllText(logPath, System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.load_captures:entry", message = "load_captures started", data = new { outDir = CaptureOutputDirectory }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H2" }) + "\n"); } catch { }
                            // #endregion
                            var urls = await Task.Run(() =>
                            {
                                // #region agent log
                                System.Diagnostics.Debug.WriteLine("[Bridge] 開始背景讀圖...");
                                try { File.AppendAllText(logPath, System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.load_captures:TaskRun_start", message = "inside Task.Run", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H2" }) + "\n"); } catch { }
                                // #endregion
                                var byIndex = CameraCaptureStore.GetCapturesByIndex();
                                var result = new string[byIndex.Count];
                                for (var i = 0; i < byIndex.Count; i++)
                                {
                                    var path = byIndex[i];
                                    var thumbPath = string.IsNullOrWhiteSpace(path) ? "" : CreateThumbnailFile(path, 800) ?? "";
                                    result[i] = string.IsNullOrEmpty(thumbPath) ? "" : $"https://photos/{Path.GetFileName(thumbPath)}";
                                }
                                // #region agent log
                                System.Diagnostics.Debug.WriteLine($"[Bridge] 讀圖完成，共 {result.Length} 張");
                                try { File.AppendAllText(logPath, System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.load_captures:TaskRun_done", message = "Task.Run completed", data = new { count = result.Length }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H2" }) + "\n"); } catch { }
                                // #endregion
                                return result;
                            }).ConfigureAwait(false);
                            // #region agent log
                            System.Diagnostics.Debug.WriteLine("[Bridge] 準備回傳前端...");
                            try { File.AppendAllText(logPath, System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.load_captures:exit", message = "load_captures returning", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H2" }) + "\n"); } catch { }
                            // #endregion
                            return Ok(req.id, new { urls });
                        }

                    case "start_liveview":
                        {
                            System.Diagnostics.Debug.WriteLine("[Live View] start_liveview 收到，開始推送到 Web…");
                            LiveViewPushToWeb = true;
                            // #region agent log
                            try
                            {
                                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log");
                                var line = JsonSerializer.Serialize(new { location = "BoothBridge.cs:start_liveview", message = "liveview_push_enabled", data = new { LiveViewPushToWeb = LiveViewPushToWeb }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H1" }) + "\n";
                                File.AppendAllText(logPath, line);
                            }
                            catch { }
                            // #endregion
                            if (CameraServiceProvider.Current is Services.CanonEdsdkCameraService edsdk && edsdk.IsDownloading)
                            {
                                System.Diagnostics.Debug.WriteLine("[Live View] start_liveview 延後：下載中，暫不啟動 EDSDK Live View。");
                                if (Interlocked.Exchange(ref _pendingStartLiveView, 1) == 0)
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            while (CameraServiceProvider.Current is Services.CanonEdsdkCameraService svc && svc.IsDownloading)
                                            {
                                                await Task.Delay(100).ConfigureAwait(false);
                                            }
                                            await CameraServiceProvider.Current.InitializeAsync().ConfigureAwait(false);
                                            await CameraServiceProvider.Current.StartLiveViewAsync().ConfigureAwait(false);
                                            System.Diagnostics.Debug.WriteLine("[Live View] start_liveview 延後啟動完成（下載結束後重試）。");
                                        }
                                        finally
                                        {
                                            Interlocked.Exchange(ref _pendingStartLiveView, 0);
                                        }
                                    });
                                }
                                return Ok(req.id, new { deferred = true, reason = "downloading" });
                            }
                            await CameraServiceProvider.Current.InitializeAsync().ConfigureAwait(false);
                            await CameraServiceProvider.Current.StartLiveViewAsync().ConfigureAwait(false);
                            System.Diagnostics.Debug.WriteLine("[Live View] start_liveview 完成，已啟動 EDSDK Live View。");
                            return Ok(req.id, new { });
                        }

                    case "stop_liveview":
                        {
                            System.Diagnostics.Debug.WriteLine("[Live View] stop_liveview 收到，停止推送到 Web。");
                            LiveViewPushToWeb = false;
                            // #region agent log
                            try
                            {
                                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log");
                                var line = JsonSerializer.Serialize(new { location = "BoothBridge.cs:stop_liveview", message = "liveview_push_disabled", data = new { LiveViewPushToWeb = LiveViewPushToWeb }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H1" }) + "\n";
                                File.AppendAllText(logPath, line);
                            }
                            catch { }
                            // #endregion
                            await CameraServiceProvider.Current.StopLiveViewAsync().ConfigureAwait(false);
                            return Ok(req.id, new { });
                        }

                    case "half_press_shutter":
                        {
                            System.Diagnostics.Debug.WriteLine("[Shoot] half_press_shutter 收到，只半按快門對焦、不拍照。");
                            await CameraServiceProvider.Current.InitializeAsync().ConfigureAwait(false);
                            await CameraServiceProvider.Current.HalfPressShutterAsync().ConfigureAwait(false);
                            return Ok(req.id, new { });
                        }

                    case "play_countdown_audio":
                        {
                            try
                            {
                                var fileName = "倒數10秒拍照.mp3";
                                if (req.data.ValueKind == System.Text.Json.JsonValueKind.Object &&
                                    req.data.TryGetProperty("fileName", out var fnEl) &&
                                    fnEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    var raw = fnEl.GetString()?.Trim() ?? "";
                                    if (raw == "倒數5秒拍照.mp3" || raw == "倒數10秒拍照.mp3")
                                        fileName = raw;
                                }

                                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                                var soundPath = Path.Combine(baseDir, "web", "assets", "templates", "music", fileName);
                                if (!File.Exists(soundPath))
                                {
                                    var altPath = Path.Combine(baseDir, "..", "web", "assets", "templates", "music", fileName);
                                    soundPath = Path.GetFullPath(altPath);
                                }
                                if (File.Exists(soundPath))
                                {
                                    _countdownPlayer?.Stop();
                                    _countdownPlayer?.Close();
                                    _countdownPlayer = new MediaPlayer();
                                    _countdownPlayer.Open(new Uri(soundPath));
                                    _countdownPlayer.Play();
                                }
                            }
                            catch { }
                            return Ok(req.id, new { });
                        }

                    case "trigger_evf_af_with_pause":
                        {
                            System.Diagnostics.Debug.WriteLine("[Shoot] trigger_evf_af_with_pause 收到，觸發 EVF 對焦。");
                            await CameraServiceProvider.Current.InitializeAsync().ConfigureAwait(false);
                            await CameraServiceProvider.Current.TriggerEvfAfWithPauseAsync().ConfigureAwait(false);
                            return Ok(req.id, new { });
                        }

                    case "get_evf_drive_focus_state":
                        {
                            var cam = CameraServiceProvider.Current;
                            return Ok(req.id, new { step = cam.EvfDriveFocusStep, maxNearSteps = cam.EvfDriveFocusMaxNearSteps });
                        }

                    case "set_evf_drive_focus_max_steps":
                        {
                            var max = 10;
                            if (req.data.TryGetProperty("maxNearSteps", out var mx) && mx.ValueKind == System.Text.Json.JsonValueKind.Number)
                                max = Math.Max(0, Math.Min(500, mx.GetInt32()));
                            CameraServiceProvider.Current.EvfDriveFocusMaxNearSteps = max;
                            var cam = CameraServiceProvider.Current;
                            return Ok(req.id, new { step = cam.EvfDriveFocusStep, maxNearSteps = cam.EvfDriveFocusMaxNearSteps });
                        }

                    case "calibrate_evf_drive_focus_far":
                        {
                            var far3 = 24;
                            if (req.data.TryGetProperty("far3RepeatCount", out var f3) && f3.ValueKind == System.Text.Json.JsonValueKind.Number)
                                far3 = Math.Max(1, Math.Min(80, f3.GetInt32()));
                            if (req.data.TryGetProperty("maxNearSteps", out var mx) && mx.ValueKind == System.Text.Json.JsonValueKind.Number)
                                CameraServiceProvider.Current.EvfDriveFocusMaxNearSteps = Math.Max(0, Math.Min(500, mx.GetInt32()));
                            await CameraServiceProvider.Current.InitializeAsync().ConfigureAwait(false);
                            await CameraServiceProvider.Current.CalibrateEvfFocusFarEndAsync(far3).ConfigureAwait(false);
                            var cam = CameraServiceProvider.Current;
                            return Ok(req.id, new { step = cam.EvfDriveFocusStep, maxNearSteps = cam.EvfDriveFocusMaxNearSteps, far3RepeatCount = far3 });
                        }

                    case "drive_evf_focus_near1":
                        {
                            await CameraServiceProvider.Current.InitializeAsync().ConfigureAwait(false);
                            var ok = await CameraServiceProvider.Current.TryDriveEvfFocusNear1Async().ConfigureAwait(false);
                            var cam = CameraServiceProvider.Current;
                            return Ok(req.id, new { ok, step = cam.EvfDriveFocusStep, maxNearSteps = cam.EvfDriveFocusMaxNearSteps });
                        }

                    case "drive_evf_focus_far1":
                        {
                            await CameraServiceProvider.Current.InitializeAsync().ConfigureAwait(false);
                            var ok = await CameraServiceProvider.Current.TryDriveEvfFocusFar1Async().ConfigureAwait(false);
                            var cam = CameraServiceProvider.Current;
                            return Ok(req.id, new { ok, step = cam.EvfDriveFocusStep, maxNearSteps = cam.EvfDriveFocusMaxNearSteps });
                        }

                    case "take_one_shot_edsdk":
                        {
                            // 1. 在 UI 執行緒解析參數 (快速)
                            var index = 0;
                            var shotCountProvided = req.data.TryGetProperty("shotCount", out var scEl) && scEl.ValueKind == System.Text.Json.JsonValueKind.Number;
                            var shotCount = shotCountProvided ? Math.Max(1, scEl.GetInt32()) : 1;
                            if (req.data.TryGetProperty("index", out var idxEl) && idxEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                                index = idxEl.GetInt32();
                            // 連拍與補拍都在拍完後自動重啟 Live View，讓使用者立即看到預覽（是否關閉交由前端 stop_liveview 控制）
                            var restartLiveViewAfter = true;
                            var outDir = CaptureOutputDirectory;

                            System.Diagnostics.Debug.WriteLine($"[Bridge] UI Thread={System.Threading.Thread.CurrentThread.ManagedThreadId} 開始處理 take_one_shot_edsdk index={index}");

                            // 2. 將繁重工作包在 Task.Run 裡，await 結束後保證回到 UI 執行緒
                            var resultData = await Task.Run(async () =>
                            {
                                System.Diagnostics.Debug.WriteLine($"[Bridge] Background Thread={System.Threading.Thread.CurrentThread.ManagedThreadId} 開始拍照");
                                try { Directory.CreateDirectory(outDir); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Shoot] 建立資料夾失敗 {outDir}: {ex.Message}"); }

                                string path = "";
                                try
                                {
                                    path = await CameraServiceProvider.Current.TakePictureAsync(outDir, index, restartLiveViewAfter);
                                }
                                catch (InvalidOperationException ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Shoot] take_one_shot_edsdk InvalidOperationException（不重試）：{ex.Message}");
                                }
                                catch (TimeoutException ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Shoot] take_one_shot_edsdk index={index} 逾時（不重試）：{ex.Message}");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Shoot] take_one_shot_edsdk 例外：{ex.GetType().Name} {ex.Message}");
                                    throw;
                                }

                                if (!string.IsNullOrWhiteSpace(path))
                                    CameraCaptureStore.SetCapture(index, path);
                                System.Diagnostics.Debug.WriteLine($"[Shoot] index={index} 拍照完成，路徑：{path ?? "(空)"}");

                                var fileName = !string.IsNullOrWhiteSpace(path) ? Path.GetFileName(path) : "";
                                var photoUrl = !string.IsNullOrEmpty(fileName) ? $"https://photos/{fileName}" : "";
                                string thumbUrl = "";
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Thumb] 開始處理: {path ?? "(空)"}");
                                    await Task.Delay(100);
                                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                                    {
                                        var thumbPath = CreateThumbnailFile(path, 600);
                                        if (!string.IsNullOrEmpty(thumbPath))
                                            thumbUrl = $"https://photos/{Path.GetFileName(thumbPath)}";
                                        System.Diagnostics.Debug.WriteLine($"[Thumb] 縮圖已存檔: {thumbPath ?? "(空)"}");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[Thumb Error] 找不到檔案: {path ?? "(空)"}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Thumb Error] 縮圖失敗: {ex.Message}");
                                }

                                return new { filePath = path ?? "", photoUrl, dataUrl = "", thumbUrl };
                            });

                            // 3. 已回到 UI 執行緒，安全回傳給 WebView2
                            System.Diagnostics.Debug.WriteLine($"[Bridge] 回到 UI Thread={System.Threading.Thread.CurrentThread.ManagedThreadId}，準備回傳 thumbUrl={resultData.thumbUrl}");
                            return Ok(req.id, resultData);
                        }

                    case "get_camera_status":
                        {
                            await CameraServiceProvider.Current.InitializeAsync().ConfigureAwait(false);
                            var isConnected = CameraServiceProvider.Current.IsConnected;
                            return Ok(req.id, new { isConnected });
                        }

                    case "open_wpf_shoot":
                        {
                            System.Diagnostics.Debug.WriteLine("[MainWindow] open_wpf_shoot 收到，切換到 WPF 拍照頁。");
                            OpenWpfShootRequested?.Invoke();
                            return Ok(req.id, new { });
                        }

                    case "upload":
                        {
                            if (!req.data.TryGetProperty("imageData", out var imageEl))
                                return RespFail(req.id, "upload 缺少 imageData");
                            var imageData = imageEl.GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(imageData))
                                return RespFail(req.id, "upload 的 imageData 是空的");
                            var (url, _, err) = await UploadToServerAsync(new { imageData }).ConfigureAwait(false);
                            if (err != null) return RespFail(req.id, err);
                            return Ok(req.id, new { url = url ?? "" });
                        }

                    case "upload_file":
                        {
                            // 從本機已存好的檔案上傳：優先使用 S3（若有 s3_config.txt），否則走 PHP API
                            var filePath = req.data.TryGetProperty("filePath", out var fpEl) ? fpEl.GetString() ?? "" : "";
                            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                                return RespFail(req.id, "upload_file 缺少有效 filePath");
                            var (url, err) = await UploadFileAsync(filePath).ConfigureAwait(false);
                            if (err != null) return RespFail(req.id, err);
                            return Ok(req.id, new { url = url ?? "" });
                        }

                    case "upload_video":
                        {
                            if (!req.data.TryGetProperty("videoData", out var videoEl))
                                return RespFail(req.id, "upload_video 缺少 videoData");
                            var videoData = videoEl.GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(videoData))
                                return RespFail(req.id, "upload_video 的 videoData 是空的");
                            var (_, videoUrl, err) = await UploadToServerAsync(new { videoData }).ConfigureAwait(false);
                            if (err != null) return RespFail(req.id, err);
                            return Ok(req.id, new { url = videoUrl ?? "" });
                        }

                    case "save_image":
                        {
                            // Vue 傳 imageData（data:image/jpeg;base64,...），相容舊的 base64
                            var base64 = "";
                            if (req.data.TryGetProperty("imageData", out var imgEl))
                                base64 = imgEl.GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(base64) && req.data.TryGetProperty("base64", out var b64El))
                                base64 = b64El.GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(base64))
                                return RespFail(req.id, "save_image 缺少 imageData 或 base64");

                            var ext = "jpg";
                            if (req.data.TryGetProperty("ext", out var extEl))
                                ext = extEl.GetString() ?? "jpg";

                            var comma = base64.IndexOf(',');
                            if (comma >= 0) base64 = base64[(comma + 1)..];

                            byte[] bytes;
                            try { bytes = Convert.FromBase64String(base64); }
                            catch (Exception ex) { return RespFail(req.id, "base64 解析失敗: " + ex.Message); }

                            var outDir = CaptureOutputDirectory;
                            Directory.CreateDirectory(outDir);
                            var path = Path.Combine(outDir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.{ext}");

                            File.WriteAllBytes(path, bytes);
                            return RespOk(req.id, new { filePath = path });
                        }

                    case "result_image_ready":
                        // 合成圖就緒，僅回傳 ok；列印改由結果頁按鈕或 60 秒自動觸發
                        return Ok(req.id, new { });

                    case "shutdown":
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "shutdown",
                                Arguments = "/s /t 0",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            });
                        }
                        catch (Exception ex)
                        {
                            return RespFail(req.id, ex.Message);
                        }
                        return Ok(req.id, new { });

                    default:
                        return RespFail(req.id, "unknown cmd");
                }
            }
            catch (Exception ex)
            {
                return RespFail(req.id, ex.Message);
            }
        }

        /// <summary>測試 Google Apps Script 連線：GET 請求。</summary>
        private static async Task<object> TestGoogleSheetGetAsync(string url)
        {
            try
            {
                var res = await _http.GetAsync(url).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new { statusCode = (int)res.StatusCode, body = body.Length > 500 ? body.Substring(0, 500) + "..." : body };
            }
            catch (Exception ex)
            {
                return new { statusCode = -1, body = "", error = ex.Message };
            }
        }

        /// <summary>測試 Google Apps Script 連線：POST 傳送 JSON 資料。</summary>
        private static async Task<object> TestGoogleSheetPostAsync(string url)
        {
            try
            {
                var payload = new
                {
                    source = "photobooth",
                    test = true,
                    time = DateTime.UtcNow.ToString("o"),
                    machineName = Environment.MachineName ?? "",
                    sample = new { date = DateTime.Today.ToString("yyyy-MM-dd"), projectName = "test", unitPrice = 100, dailySalesCount = 1, dailyRevenue = 100m }
                };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await _http.PostAsync(url, content).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new { statusCode = (int)res.StatusCode, body = body.Length > 500 ? body.Substring(0, 500) + "..." : body };
            }
            catch (Exception ex)
            {
                return new { statusCode = -1, body = "", error = ex.Message };
            }
        }

        private static (string date, string time) ParseDateTimeParts(string? printTime)
        {
            if (string.IsNullOrWhiteSpace(printTime))
            {
                var n = DateTime.Now;
                return (n.ToString("yyyy-MM-dd"), n.ToString("HH:mm:ss"));
            }
            if (DateTime.TryParse(printTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                if (dt.Kind == DateTimeKind.Utc)
                    dt = dt.ToLocalTime();
                return (dt.ToString("yyyy-MM-dd"), dt.ToString("HH:mm:ss"));
            }
            var now = DateTime.Now;
            return (now.ToString("yyyy-MM-dd"), now.ToString("HH:mm:ss"));
        }

        /// <summary>通用上傳：將資料 POST 至 Google Apps Script，payload 為 { targetSheet, data }。成功回傳 null，失敗回傳錯誤訊息。</summary>
        public static async Task<string?> UploadDataAsync(string sheetName, object dataObj)
        {
            // #region agent log
            var dataCount = dataObj is System.Collections.IList list ? list.Count : 1;
            try
            {
                var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                var lineIn = System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.cs:UploadDataAsync:entry", message = "UploadDataAsync called", data = new { sheetName, dataCount, url = GoogleScriptUrl }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H4" }) + "\n";
                File.AppendAllText(logPath, lineIn);
            }
            catch { }
            // #endregion
            try
            {
                var payload = new { targetSheet = sheetName, data = dataObj };
                var json = JsonSerializer.Serialize(payload);
                // #region agent log
                try
                {
                    var logPathPay = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                    var payloadPreview = json.Length > 500 ? json.Substring(0, 500) + "..." : json;
                    var linePay = System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.cs:UploadDataAsync:payload", message = "outgoing JSON preview", data = new { sheetName, jsonLen = json.Length, payloadPreview }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H6" }) + "\n";
                    File.AppendAllText(logPathPay, linePay);
                }
                catch { }
                // #endregion
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await _http.PostAsync(GoogleScriptUrl, content).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                // #region agent log
                try
                {
                    var logPath2 = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                    var lineOut = System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.cs:UploadDataAsync:response", message = "Google response", data = new { sheetName, statusCode = (int)res.StatusCode, bodyLen = body?.Length ?? 0, bodyPreview = body?.Length > 0 ? (body.Length > 200 ? body.Substring(0, 200) + "..." : body) : "" }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H3" }) + "\n";
                    File.AppendAllText(logPath2, lineOut);
                }
                catch { }
                // #endregion
                if (!res.IsSuccessStatusCode)
                    return $"HTTP {(int)res.StatusCode}: {body}";
                return null;
            }
            catch (Exception ex)
            {
                // #region agent log
                try
                {
                    var logPath3 = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
                    var lineEx = System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.cs:UploadDataAsync:catch", message = "UploadDataAsync exception", data = new { sheetName, error = ex.Message }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H3" }) + "\n";
                    File.AppendAllText(logPath3, lineEx);
                }
                catch { }
                // #endregion
                return ex.Message;
            }
        }

        private static string RespOk(string id, object data)
            => System.Text.Json.JsonSerializer.Serialize(new { id, ok = true, data });

        private static string RespFail(string id, string error)
            => System.Text.Json.JsonSerializer.Serialize(new { id, ok = false, error });


        private static string Ok(string id, object data)
    => System.Text.Json.JsonSerializer.Serialize(new { id, ok = true, data });

        private static string Fail(string id, string error)
            => System.Text.Json.JsonSerializer.Serialize(new { id, ok = false, error });

        private static bool TryGetString(System.Text.Json.JsonElement data, string name, out string value)
        {
            value = "";
            if (data.ValueKind == System.Text.Json.JsonValueKind.Undefined) return false;
            if (!data.TryGetProperty(name, out var el)) return false;
            value = el.GetString() ?? "";
            return true;
        }

        /// <summary>上傳檔案：若有 s3_config.txt 則上傳到 S3，否則走 PHP API。</summary>
        private static async Task<(string? url, string? error)> UploadFileAsync(string filePath)
        {
            var s3Config = LoadS3Config();
            if (s3Config != null)
            {
                var (url, err) = await UploadToS3Async(filePath, s3Config.Value).ConfigureAwait(false);
                if (err == null) return (url, null);
                System.Diagnostics.Debug.WriteLine($"[BoothBridge] S3 上傳失敗，改走 PHP API: {err}");
            }
            var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            var mime = ext switch { "png" => "image/png", "gif" => "image/gif", _ => "image/jpeg" };
            var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            var base64 = Convert.ToBase64String(bytes);
            var imageData = $"data:{mime};base64,{base64}";
            var (phpUrl, _, phpErr) = await UploadToServerAsync(new { imageData }).ConfigureAwait(false);
            return (phpUrl, phpErr);
        }

        private static (string AccessKey, string SecretKey, string Bucket, string Region)? LoadS3Config()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var paths = new[]
            {
                Path.Combine(baseDir, "s3_config.txt"),
                Path.Combine(baseDir, "..", "s3_config.txt"),
                Path.Combine(baseDir, "..", "..", "..", "..", "s3_config.txt"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "s3_config.txt"),
            };
            foreach (var p in paths)
            {
                var full = Path.GetFullPath(p);
                if (!File.Exists(full)) continue;
                try
                {
                    var text = File.ReadAllText(full);
                    var lines = text.Split('\n', '\r');
                    string? accessKey = null, secretKey = null, bucket = null, region = "ap-northeast-1";
                    foreach (var line in lines)
                    {
                        var t = line.Trim();
                        if (t.StartsWith("#") || string.IsNullOrEmpty(t)) continue;
                        var eq = t.IndexOf('=');
                        if (eq <= 0) continue;
                        var key = t[..eq].Trim();
                        var val = t[(eq + 1)..].Trim();
                        if (key.Equals("AccessKey", StringComparison.OrdinalIgnoreCase)) accessKey = val;
                        else if (key.Equals("SecretKey", StringComparison.OrdinalIgnoreCase)) secretKey = val;
                        else if (key.Equals("Bucket", StringComparison.OrdinalIgnoreCase)) bucket = val;
                        else if (key.Equals("Region", StringComparison.OrdinalIgnoreCase)) region = val;
                    }
                    if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey) && !string.IsNullOrWhiteSpace(bucket))
                        return (accessKey, secretKey, bucket, region ?? "ap-northeast-1");
                }
                catch { /* 略過 */ }
            }
            return null;
        }

        private static async Task<(string? url, string? error)> UploadToS3Async(string filePath, (string AccessKey, string SecretKey, string Bucket, string Region) config)
        {
            try
            {
                var region = RegionEndpoint.GetBySystemName(config.Region);
                using var client = new AmazonS3Client(config.AccessKey, config.SecretKey, region);
                var fileName = Path.GetFileName(filePath);
                var key = $"photobooth/{DateTime.UtcNow:yyyyMMdd}/{DateTime.UtcNow:HHmmss}_{Path.GetFileNameWithoutExtension(fileName)}{Path.GetExtension(fileName)}";
                var putRequest = new PutObjectRequest
                {
                    BucketName = config.Bucket,
                    Key = key,
                    FilePath = filePath,
                    ContentType = Path.GetExtension(filePath).ToLowerInvariant() switch { ".png" => "image/png", ".gif" => "image/gif", _ => "image/jpeg" },
                };
                await client.PutObjectAsync(putRequest).ConfigureAwait(false);
                var preSignedRequest = new GetPreSignedUrlRequest
                {
                    BucketName = config.Bucket,
                    Key = key,
                    Expires = DateTime.UtcNow.AddHours(24),
                };
                var url = client.GetPreSignedURL(preSignedRequest);
                return (url, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>POST JSON 到上傳 API（與 Vue 的 PHP upload.php 格式一致），回傳 (url, videoUrl, error)。</summary>
        private static async Task<(string? url, string? videoUrl, string? error)> UploadToServerAsync(object payload)
        {
            var url = UploadUrl;
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_url.txt");
                if (File.Exists(configPath))
                {
                    var custom = File.ReadAllText(configPath).Trim();
                    if (!string.IsNullOrWhiteSpace(custom)) url = custom;
                }
            }
            catch { /* 沿用預設 */ }

            if (string.IsNullOrWhiteSpace(url))
                return (null, null, "未設定上傳網址（UploadUrl 或 upload_url.txt）");

            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await _http.PostAsync(url, content).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    var preview = (body ?? "").Length > 200 ? (body ?? "").Substring(0, 200) + "…" : (body ?? "");
                    return (null, null, $"上傳失敗: {res.StatusCode} {preview}");
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var imageUrl = root.TryGetProperty("url", out var u) ? u.GetString() : null;
                var vidUrl = root.TryGetProperty("videoUrl", out var v) ? v.GetString() : null;
                return (imageUrl, vidUrl ?? imageUrl, null);
            }
            catch (Exception ex)
            {
                return (null, null, ex.Message);
            }
        }

        private static string CreatePhotoUrlOrDataUrl(string filePath, string outDir)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return "";
            var dir = Path.GetFullPath(Path.GetDirectoryName(filePath) ?? "");
            var targetDir = Path.GetFullPath(outDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (dir.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(filePath);
                return string.IsNullOrEmpty(fileName) ? "" : $"https://photos/{fileName}";
            }
            // #region agent log
            var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
            try { File.AppendAllText(logPath, System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.CreatePhotoUrlOrDataUrl:fallback_thumbnail", message = "path mismatch, calling CreateThumbnailDataUrl", data = new { filePath }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H3" }) + "\n"); } catch { }
            // #endregion
            // 治本：路徑不符時強制用縮圖，絕不讀取 DSLR 原圖轉 Base64
            return CreateThumbnailDataUrl(filePath, 1920);
        }

        /// <summary>產生縮圖並存成 _thumb.jpg 實體檔，前端用 thumbUrl 載入可避免 OOM。</summary>
        private static string CreateThumbnailFile(string filePath, int maxSize)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return "";
                var dir = Path.GetDirectoryName(filePath) ?? "";
                var thumbPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(filePath) + "_thumb.jpg");
                using var img = Image.FromFile(filePath);
                var w = img.Width;
                var h = img.Height;
                var nw = w <= maxSize && h <= maxSize ? w : (int)(w * Math.Min((double)maxSize / w, (double)maxSize / h));
                var nh = w <= maxSize && h <= maxSize ? h : (int)(h * Math.Min((double)maxSize / w, (double)maxSize / h));
                using var thumb = new Bitmap(nw, nh);
                using (var g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, nw, nh);
                }
                var jpegEncoder = GetJpegEncoder();
                if (jpegEncoder != null)
                {
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
                    thumb.Save(thumbPath, jpegEncoder, encoderParams);
                }
                else
                    thumb.Save(thumbPath, ImageFormat.Jpeg);
                return thumbPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Thumb File Error] {ex.Message}");
                return "";
            }
        }

        private static string CreateThumbnailDataUrl(string filePath, int maxSize)
        {
            var logPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return "";
                // #region agent log
                System.Diagnostics.Debug.WriteLine($"[Thumb] 處理: {filePath}");
                try { File.AppendAllText(logPath, System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.CreateThumbnailDataUrl:before_FromFile", message = "about to Image.FromFile", data = new { filePath }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H3" }) + "\n"); } catch { }
                // #endregion
                using var img = Image.FromFile(filePath);
                var w = img.Width;
                var h = img.Height;
                var nw = w <= maxSize && h <= maxSize ? w : (int)(w * Math.Min((double)maxSize / w, (double)maxSize / h));
                var nh = w <= maxSize && h <= maxSize ? h : (int)(h * Math.Min((double)maxSize / w, (double)maxSize / h));
                using var thumb = new Bitmap(nw, nh);
                using (var g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, nw, nh);
                }
                var jpegEncoder = GetJpegEncoder();
                using var ms = new MemoryStream();
                if (jpegEncoder != null)
                {
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
                    thumb.Save(ms, jpegEncoder, encoderParams);
                }
                else
                    thumb.Save(ms, ImageFormat.Jpeg);
                return $"data:image/jpeg;base64,{Convert.ToBase64String(ms.ToArray())}";
            }
            catch (Exception ex)
            {
                // #region agent log
                System.Diagnostics.Debug.WriteLine($"[Thumb Error] {ex.Message}");
                try { File.AppendAllText(logPath, System.Text.Json.JsonSerializer.Serialize(new { location = "BoothBridge.CreateThumbnailDataUrl:catch", message = "exception", data = new { filePath, error = ex.Message, type = ex.GetType().Name }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H3" }) + "\n"); } catch { }
                // #endregion
                return "";
            }
        }

        private static ImageCodecInfo? GetJpegEncoder()
        {
            return ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
        }

        private static string CreateDataUrl(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return "";
                var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
                var mime = ext switch { "png" => "image/png", "gif" => "image/gif", _ => "image/jpeg" };
                var bytes = File.ReadAllBytes(filePath);
                var base64 = Convert.ToBase64String(bytes);
                return $"data:{mime};base64,{base64}";
            }
            catch
            {
                return "";
            }
        }
    }
}

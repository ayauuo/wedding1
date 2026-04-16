using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhotoBoothWin.Services
{
    public sealed class CanonEdsdkCameraService : ICameraService
    {
        private const string DebugLogPath = @"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log";

        /// <summary>Static callback 與 GC.KeepAlive 可防止 delegate 被 GC 回收，EDSDK 才能正確觸發 OnObjectEvent。</summary>
        private static readonly EdsObjectEventHandler S_objectEventHandler = OnObjectEventStatic;

        private readonly object _sync = new();
        private IntPtr _cameraList = IntPtr.Zero;
        private IntPtr _camera = IntPtr.Zero;
        private bool _initialized;
        private bool _sessionOpen;

        private CancellationTokenSource? _liveViewCts;
        private Task? _liveViewTask;

        private TaskCompletionSource<string>? _pendingCaptureTcs;
        private string _pendingCaptureDir = "";
        private int _pendingCaptureIndex;
        private volatile bool _isDownloading;

        /// <summary>AF 前暫停 EVF 拉流，避免 DoEvfAf 時相機 Busy。</summary>
        private volatile bool _pauseEvfPull;

        /// <summary>自 <see cref="CalibrateEvfFocusFarEndAsync"/> 後，Near1 成功累計步數（僅軟體計步，手轉對焦環會不同步）。</summary>
        private int _evfDriveFocusStep;

        private int _evfDriveFocusMaxNearSteps = 10;

        public event EventHandler<BitmapSource>? LiveViewFrameReady;
        public event EventHandler<string>? PhotoCaptured;

        public bool IsConnected => _sessionOpen;
        public bool IsDownloading => _isDownloading;

        /// <summary>EDSDK 需在具訊息幫浦的 STA 執行緒初始化和呼叫 EdsGetEvent，故改在 UI 執行緒執行以符合「門戶對齊」。</summary>
        public async Task InitializeAsync()
        {
            if (_initialized) return;
            var dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("WPF Dispatcher 尚未就緒，無法初始化 EDSDK。");
            await dispatcher.InvokeAsync(() =>
            {
                lock (_sync)
                {
                    if (_initialized) return;
                    EnsureSuccess(EdsInitializeSDK(), "EdsInitializeSDK");
                    EnsureSuccess(EdsGetCameraList(out _cameraList), "EdsGetCameraList");
                    EnsureSuccess(EdsGetChildAtIndex(_cameraList, 0, out _camera), "EdsGetChildAtIndex");
                    EnsureSuccess(EdsOpenSession(_camera), "EdsOpenSession");
                    _sessionOpen = true;

                    // 入門機 (如 EOS 4000D) 鎖定 UI 可避免按鈕/測光中斷 SDK 通訊
                    try { EdsSendStatusCommand(_camera, (uint)EdsCameraStatusCommand.UILock, 1); } catch { }

                    // 必須開啟遠端拍攝模式，相機才會在 TakePicture 後觸發 DirItemRequestTransfer 傳送照片給 PC
                    var remoteErr = EdsSendCommand(_camera, (uint)EdsCameraCommand.SetRemoteShootingMode, 1);
                    // #region agent log
                    try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:InitializeAsync", message = "SetRemoteShootingMode Start", data = new { errHex = "0x" + remoteErr.ToString("X"), ok = (remoteErr == (uint)EdsError.OK) }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "RemoteShooting" }) + "\n"); } catch { }
                    // #endregion
                    if (remoteErr != (uint)EdsError.OK)
                        System.Diagnostics.Debug.WriteLine($"[Shoot] SetRemoteShootingMode(Start) 回傳 0x{remoteErr:X}，部分機型仍可拍照");

                    var saveTo = (uint)EdsSaveTo.Host;
                    EnsureSuccess(EdsSetPropertyData(_camera, (uint)EdsPropertyID.SaveTo, 0, sizeof(uint), ref saveTo), "EdsSetPropertyData(SaveTo)");
                    var capacity = new EdsCapacity
                    {
                        NumberOfFreeClusters = 0x7FFFFFFF,
                        BytesPerSector = 0x1000, // 4096，部分 4000D 韌體在 SaveTo.Host 下需較大 sector
                        Reset = 1
                    };
                    EnsureSuccess(EdsSetCapacity(_camera, capacity), "EdsSetCapacity");

                    EnsureSuccess(EdsSetObjectEventHandler(_camera, (uint)EdsObjectEvent.All, S_objectEventHandler, IntPtr.Zero), "EdsSetObjectEventHandler");
                    GC.KeepAlive(S_objectEventHandler);
                    _initialized = true;
                }
            }).Task.ConfigureAwait(false);
        }

        public async Task StartLiveViewAsync()
        {
            await InitializeAsync().ConfigureAwait(false);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    lock (_sync)
                    {
                        if (_liveViewTask != null) return;
                        var mode = 1u;
                        EnsureSuccess(EdsSetPropertyData(_camera, (uint)EdsPropertyID.EvfMode, 0, sizeof(uint), ref mode), "EdsSetPropertyData(EvfMode)");
                        var device = (uint)EdsEvfOutputDevice.PC;
                        EnsureSuccess(EdsSetPropertyData(_camera, (uint)EdsPropertyID.EvfOutputDevice, 0, sizeof(uint), ref device), "EdsSetPropertyData(EvfOutputDevice)");
                        // 對焦模式：預覽時不啟用持續對焦，避免拍完後鏡頭持續抽動影響下一輪預覽
                        var evfAfMode = (uint)EdsEvfAFMode.Live;
                        try
                        {
                            EnsureSuccess(EdsSetPropertyData(_camera, (uint)EdsPropertyID.EvfAFMode, 0, sizeof(uint), ref evfAfMode), "EdsSetPropertyData(EvfAFMode)");
                        }
                        catch
                        {
                            // 部分機型不支援或僅支援 Quick，不影響 Live View 顯示
                        }
                        // 不在啟動 Live View 時強制開啟連續對焦；倒數時再用 trigger_evf_af_with_pause 觸發即可
                        try
                        {
                            EdsSendCommand(_camera, (uint)EdsCameraCommand.DoEvfAf, (int)EdsEvfAf.Off);
                        }
                        catch { }
                        // #region agent log
                        try
                        {
                            File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new
                            {
                                location = "CanonEdsdkCameraService.cs:StartLiveViewAsync",
                                message = "start_liveview_begin_loop",
                                data = new { },
                                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                sessionId = "debug-session",
                                runId = "run1",
                                hypothesisId = "H3"
                            }) + "\n");
                        }
                        catch { }
                        // #endregion
                        _liveViewCts = new CancellationTokenSource();
                        _liveViewTask = Task.Run(() => LiveViewLoop(_liveViewCts.Token));
                    }
                    return;
                }
                catch when (attempt == 0)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                }
            }
        }

        public async Task StopLiveViewAsync()
        {
            CancellationTokenSource? cts;
            Task? task;
            lock (_sync)
            {
                cts = _liveViewCts;
                task = _liveViewTask;
                _liveViewCts = null;
                _liveViewTask = null;
            }
            if (cts != null)
            {
                cts.Cancel();
                try { if (task != null) await task.ConfigureAwait(false); } catch { }
                cts.Dispose();
            }
            if (_sessionOpen)
            {
                // #region agent log
                try
                {
                    File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new
                    {
                        location = "CanonEdsdkCameraService.cs:StopLiveViewAsync",
                        message = "stop_liveview_end_loop",
                        data = new { },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        sessionId = "debug-session",
                        runId = "run1",
                        hypothesisId = "H4"
                    }) + "\n");
                }
                catch { }
                // #endregion
                try { EdsSendCommand(_camera, (uint)EdsCameraCommand.DoEvfAf, (int)EdsEvfAf.Off); } catch { }
                try
                {
                    var device = (uint)EdsEvfOutputDevice.None;
                    EdsSetPropertyData(_camera, (uint)EdsPropertyID.EvfOutputDevice, 0, sizeof(uint), ref device);
                }
                catch { /* 已停止時可能失敗，忽略 */ }
            }
        }

        /// <summary>半按快門觸發對焦再放開，部分機型需此步驟才會對焦。</summary>
        public async Task HalfPressShutterAsync()
        {
            await InitializeAsync().ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Halfway);
                }
                await Task.Delay(600).ConfigureAwait(false);
                lock (_sync)
                {
                    EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Off);
                }
            }
            catch
            {
                // 部分機型不支援時仍可拍照
            }
        }

        /// <summary>開始持續半按快門（對焦鎖定），倒數期間保持；倒數結束後須呼叫 EndHalfPressAsync 放開。</summary>
        public async Task StartHalfPressAsync()
        {
            await InitializeAsync().ConfigureAwait(false);
            // #region agent log
            try
            {
                var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                File.AppendAllText(@"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log",
                    "{\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"H4\",\"location\":\"CanonEdsdkCameraService.cs:StartHalfPressAsync\",\"message\":\"StartHalfPressAsync_entered_sending_Halfway_once\",\"data\":{},\"timestamp\":" + t + "}\n");
            }
            catch { }
            // #endregion
            try
            {
                lock (_sync)
                {
                    EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Halfway);
                }
            }
            catch
            {
                // 部分機型不支援時仍可拍照
            }
        }

        /// <summary>結束持續半按快門（放開快門鈕）。</summary>
        public async Task EndHalfPressAsync()
        {
            // #region agent log
            try
            {
                var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                File.AppendAllText(@"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log",
                    "{\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"H4\",\"location\":\"CanonEdsdkCameraService.cs:EndHalfPressAsync\",\"message\":\"EndHalfPressAsync_entered_sending_Off\",\"data\":{},\"timestamp\":" + t + "}\n");
            }
            catch { }
            // #endregion
            try
            {
                lock (_sync)
                {
                    EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Off);
                }
            }
            catch
            {
                // 部分機型不支援時仍可拍照
            }
        }

        /// <summary>觸發一次 EVF 自動對焦（先 Off 再 On，讓相機接受下一次對焦）。</summary>
        public async Task TriggerEvfAfAsync()
        {
            await InitializeAsync().ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    var errOff = EdsSendCommand(_camera, (uint)EdsCameraCommand.DoEvfAf, (int)EdsEvfAf.Off);
                    if (errOff != (uint)EdsError.OK) return;
                }
                await Task.Delay(120).ConfigureAwait(false);
                lock (_sync)
                {
                    var errOn = EdsSendCommand(_camera, (uint)EdsCameraCommand.DoEvfAf, (int)EdsEvfAf.On);
                    if (errOn != (uint)EdsError.OK) return;
                }
            }
            catch
            {
                // 裝置忙碌或部分機型不支援時不拋出
            }
        }

        /// <summary>暫停 LiveView 拉流 200ms → 觸發 AF → 等鏡頭 350ms，再恢復拉流；減少 Busy，建議在 T=10/3/1 秒各呼叫一次。</summary>
        public async Task TriggerEvfAfWithPauseAsync()
        {
            _pauseEvfPull = true;
            try
            {
                await Task.Delay(200).ConfigureAwait(false);
                await TriggerEvfAfAsync().ConfigureAwait(false);
                await Task.Delay(350).ConfigureAwait(false);
            }
            finally
            {
                _pauseEvfPull = false;
            }
        }

        public async Task AutoFocusAsync()
        {
            await InitializeAsync().ConfigureAwait(false);
            lock (_sync)
            {
                EnsureSuccess(EdsSendCommand(_camera, (uint)EdsCameraCommand.DoEvfAf, (int)EdsEvfAf.Off), "EdsSendCommand(DoEvfAf)");
            }
        }

        public int EvfDriveFocusStep
        {
            get { lock (_sync) { return _evfDriveFocusStep; } }
        }

        public int EvfDriveFocusMaxNearSteps
        {
            get { lock (_sync) { return _evfDriveFocusMaxNearSteps; } }
            set
            {
                var v = Math.Max(0, Math.Min(500, value));
                lock (_sync) { _evfDriveFocusMaxNearSteps = v; }
            }
        }

        public async Task<bool> TryDriveEvfFocusNear1Async()
        {
            await InitializeAsync().ConfigureAwait(false);
            bool moved = false;
            lock (_sync)
            {
                if (_evfDriveFocusStep >= _evfDriveFocusMaxNearSteps)
                {
                    System.Diagnostics.Debug.WriteLine("[DriveLens] Near1 略過：已達 maxNearSteps");
                    return false;
                }
                var err = EdsSendCommand(_camera, (uint)EdsCameraCommand.DriveLensEvf, (int)EdsEvfDriveLens.Near1);
                if (err != (uint)EdsError.OK)
                {
                    System.Diagnostics.Debug.WriteLine($"[DriveLens] Near1 failed: 0x{err:X}");
                    return false;
                }
                _evfDriveFocusStep++;
                moved = true;
            }
            if (moved)
                await Task.Delay(80).ConfigureAwait(false);
            return moved;
        }

        public async Task<bool> TryDriveEvfFocusFar1Async()
        {
            await InitializeAsync().ConfigureAwait(false);
            bool moved = false;
            lock (_sync)
            {
                if (_evfDriveFocusStep <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("[DriveLens] Far1 略過：已在步數下限 0");
                    return false;
                }
                var err = EdsSendCommand(_camera, (uint)EdsCameraCommand.DriveLensEvf, (int)EdsEvfDriveLens.Far1);
                if (err != (uint)EdsError.OK)
                {
                    System.Diagnostics.Debug.WriteLine($"[DriveLens] Far1 failed: 0x{err:X}");
                    return false;
                }
                _evfDriveFocusStep--;
                moved = true;
            }
            if (moved)
                await Task.Delay(80).ConfigureAwait(false);
            return moved;
        }

        public async Task CalibrateEvfFocusFarEndAsync(int far3RepeatCount = 24)
        {
            await InitializeAsync().ConfigureAwait(false);
            var n = Math.Max(1, Math.Min(80, far3RepeatCount));
            for (var i = 0; i < n; i++)
            {
                lock (_sync)
                {
                    EdsSendCommand(_camera, (uint)EdsCameraCommand.DriveLensEvf, (int)EdsEvfDriveLens.Far3);
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            lock (_sync)
            {
                _evfDriveFocusStep = 0;
            }
        }

        public async Task<string> TakePictureAsync(string outputDir, int index, bool restartLiveViewAfter = true)
        {
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new ArgumentException("outputDir is required.", nameof(outputDir));

            // #region agent log
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log");
                var line = JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:TakePictureAsync", message = "entry", data = new { index }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", hypothesisId = "H1" }) + "\n";
                File.AppendAllText(logPath, line);
            }
            catch { }
            // #endregion

            await InitializeAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: outputDir={outputDir} index={index}");
            try { Directory.CreateDirectory(outputDir); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: 建立資料夾失敗 {outputDir}: {ex.Message}"); }

            System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: 停止 Live View…");
            await StopLiveViewAsync().ConfigureAwait(false);
            // #region agent log
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log");
                var line = JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:TakePictureAsync", message = "after StopLiveView", data = new { index }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", hypothesisId = "H2" }) + "\n";
                File.AppendAllText(logPath, line);
            }
            catch { }
            // #endregion
            // 4000D 等機型：停止 Live View 後反光鏡需時間歸位，太早發 TakePicture 會被相機忽略
            await Task.Delay(1000).ConfigureAwait(false);

            lock (_sync)
            {
                var saveTo = (uint)EdsSaveTo.Host;
                var err = EdsSetPropertyData(_camera, (uint)EdsPropertyID.SaveTo, 0, sizeof(uint), ref saveTo);
                System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: SaveTo=Host 設定結果 0x{err:X}");
                var capacity = new EdsCapacity { NumberOfFreeClusters = 0x7FFFFFFF, BytesPerSector = 0x1000, Reset = 1 };
                var capErr = EdsSetCapacity(_camera, capacity);
                System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: SetCapacity 結果 0x{capErr:X}");
            }

            TaskCompletionSource<string> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _pendingCaptureDir = outputDir;
                _pendingCaptureIndex = index;
                _pendingCaptureTcs = tcs;
            }

            // 拍照前關閉 EVF 自動對焦，讓快門不等待對焦即可釋放（倒數期間仍會觸發對焦，到此格才強制不等待）
            lock (_sync)
            {
                try { EdsSendCommand(_camera, (uint)EdsCameraCommand.DoEvfAf, (int)EdsEvfAf.Off); } catch { }
            }
            await Task.Delay(80).ConfigureAwait(false);

            // 部分機型在 EVF 關閉後才接受 SetRemoteShootingMode；若 init 時回傳 0x60 可在此再試一次
            var remoteErr2 = EdsSendCommand(_camera, (uint)EdsCameraCommand.SetRemoteShootingMode, 1);
            // #region agent log
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:TakePictureAsync", message = "SetRemoteShootingMode before TakePicture", data = new { errHex = "0x" + remoteErr2.ToString("X"), ok = (remoteErr2 == (uint)EdsError.OK) }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "RemoteShooting" }) + "\n"); } catch { }
            // #endregion

            const uint errAfNg = 0x8D01;
            var afStartMs = Environment.TickCount64;
            const int afTimeoutMs = 2000;
            // 先嘗試 TakePicture：部分機型 (如 EOS 4000D) 僅在 TakePicture 時觸發 DirItemRequestTransfer，PressShutterButton 不會觸發
            System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: 發送 TakePicture…");
            uint captureErr;
            lock (_sync)
            {
                captureErr = EdsSendCommand(_camera, (uint)EdsCameraCommand.TakePicture, 0);
            }
            if (captureErr != (uint)EdsError.OK)
            {
                System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: TakePicture 回傳 0x{captureErr:X}，改試 PressShutterButton Completely…");
                bool shutterOk = false;
                for (int attempt = 0; attempt < 3 && !shutterOk; attempt++)
                {
                    if (attempt > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: 重試 attempt={attempt}…");
                        await Task.Delay(500 * attempt).ConfigureAwait(false);
                    }
                    uint err;
                    lock (_sync)
                    {
                        err = EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Completely);
                        if (err == (uint)EdsError.OK)
                        {
                            System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: PressShutterButton 已送出，等待 OnObjectEvent…");
                            // #region agent log
                            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:TakePictureAsync", message = "PressShutterButton sent OK, wait OnObjectEvent", data = new { index }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H1" }) + "\n"); } catch { }
                            // #endregion
                            shutterOk = true;
                            break;
                        }
                        System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: PressShutterButton 回傳 0x{err:X}");
                        if (err == errAfNg)
                            EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Off);
                    }
                    if (shutterOk) break;
                    if (err == errAfNg)
                    {
                        if (Environment.TickCount64 - afStartMs >= afTimeoutMs)
                        {
                            System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: AF_NG 超過 2 秒，放棄拍照。");
                            lock (_sync)
                            {
                                EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Off);
                            }
                            await StartLiveViewAsync().ConfigureAwait(false);
                            throw new TimeoutException("AF timeout (2s).");
                        }
                        await Task.Delay(200).ConfigureAwait(false);
                        System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: 0x8D01(AF_NG)，先半按對焦再全按…");
                        lock (_sync) { EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Halfway); }
                        await Task.Delay(500).ConfigureAwait(false);
                        lock (_sync) { EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Off); }
                        await Task.Delay(200).ConfigureAwait(false);
                        lock (_sync)
                        {
                            err = EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Completely);
                            if (err == (uint)EdsError.OK)
                            {
                                System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: 半按後全按已送出，等待 OnObjectEvent…");
                                shutterOk = true;
                                break;
                            }
                            System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: 半按後全按仍回傳 0x{err:X}");
                        }
                    }
                    if (attempt == 2) EnsureSuccess(err, "EdsSendCommand(PressShutterButton)");
                }
                lock (_sync)
                {
                    EdsSendCommand(_camera, (uint)EdsCameraCommand.PressShutterButton, (int)EdsShutterButton.Off);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: TakePicture 已送出，等待 OnObjectEvent（請勿關閉程式，最多 60 秒）…");
                // #region agent log
                try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:TakePictureAsync", message = "TakePicture sent OK, wait OnObjectEvent", data = new { index }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H1" }) + "\n"); } catch { }
                // #endregion
            }

            string path;
            const int captureTimeoutSeconds = 60;
            // #region agent log
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:TakePictureAsync", message = "waiting_tcs", data = new { index }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H1" }) + "\n"); } catch { }
            // #endregion
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(captureTimeoutSeconds));
            var uiDispatcher = Application.Current?.Dispatcher;
            // 等待迴圈：EdsGetEvent() 與 PushFrame 必須在 UI 執行緒執行（與 EdsInitializeSDK 同執行緒），SDK 的訊息幫浦才能收到相機事件
            while (!cts.Token.IsCancellationRequested)
            {
                if (uiDispatcher != null)
                {
                    await uiDispatcher.InvokeAsync(() =>
                    {
                        try { EdsGetEvent(); } catch { /* 忽略單次錯誤 */ }
                        DoEventsWpf(); // WPF 版 DoEvents，讓 COM/SDK 訊息被處理
                    }).Task.ConfigureAwait(false);
                }
                else
                {
                    try { EdsGetEvent(); } catch { }
                }
                if (tcs.Task.IsCompleted)
                    break;
                try { await Task.Delay(200, cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
            if (!tcs.Task.IsCompleted)
            {
                System.Diagnostics.Debug.WriteLine($"[Shoot] TakePictureAsync: 逾時 {captureTimeoutSeconds} 秒，相機未傳送照片。預定儲存路徑：{_pendingCaptureDir}（未寫入任何檔案）");
                try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:TakePictureAsync", message = "timeout fired", data = new { index = _pendingCaptureIndex, pendingDir = _pendingCaptureDir, timeoutSec = captureTimeoutSeconds }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H1" }) + "\n"); } catch { }
                tcs.TrySetException(new TimeoutException($"Capture timeout ({captureTimeoutSeconds}s)."));
            }
            path = await tcs.Task.ConfigureAwait(false);

            if (restartLiveViewAfter)
            {
                System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: 準備下一張，重新啟動 Live View…");
                await Task.Delay(200).ConfigureAwait(false);
                await StartLiveViewAsync().ConfigureAwait(false);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Shoot] TakePictureAsync: 最後一張已完成，保持 Live View 關閉。");
            }
            return path;
        }

        /// <summary>WPF 版 DoEvents：在當前 Dispatcher 上處理一輪訊息佇列，讓 COM/SDK 事件有機會被消化。僅在 UI 執行緒的 InvokeAsync 內呼叫。</summary>
        private static void DoEventsWpf()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(arg => { ((DispatcherFrame)arg!).Continue = false; return null; }), frame);
            Dispatcher.PushFrame(frame);
        }

        private void LiveViewLoop(CancellationToken token)
        {
            var frameCount = 0;
            var errCount = 0;
            while (!token.IsCancellationRequested)
            {
                while (_pauseEvfPull) { Thread.Sleep(50); }
                var stream = IntPtr.Zero;
                var evfImage = IntPtr.Zero;
                try
                {
                    EnsureSuccess(EdsCreateMemoryStream(0, out stream), "EdsCreateMemoryStream");
                    EnsureSuccess(EdsCreateEvfImageRef(stream, out evfImage), "EdsCreateEvfImageRef");
                    var err = EdsDownloadEvfImage(_camera, evfImage);
                    if (err != (uint)EdsError.OK)
                    {
                        ReleaseQuietly(evfImage);
                        ReleaseQuietly(stream);
                        if (err != (uint)EdsError.ObjectNotReady)
                        {
                            errCount++;
                            if (errCount == 1 || errCount % 20 == 0)
                            {
                                // #region agent log
                                try
                                {
                                    File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new
                                    {
                                        location = "CanonEdsdkCameraService.cs:LiveViewLoop",
                                        message = "evf_download_error",
                                        data = new { errHex = "0x" + err.ToString("X") },
                                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                        sessionId = "debug-session",
                                        runId = "run1",
                                        hypothesisId = "H3"
                                    }) + "\n");
                                }
                                catch { }
                                // #endregion
                            }
                        }
                        Thread.Sleep(err == (uint)EdsError.ObjectNotReady ? 30 : 80);
                        continue;
                    }
                    EnsureSuccess(EdsGetLength(stream, out var length), "EdsGetLength");
                    EnsureSuccess(EdsGetPointer(stream, out var dataPtr), "EdsGetPointer");
                    if (length > 0 && dataPtr != IntPtr.Zero)
                    {
                        var bytes = new byte[length];
                        Marshal.Copy(dataPtr, bytes, 0, (int)length);
                        using var ms = new MemoryStream(bytes);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        LiveViewFrameReady?.Invoke(this, bmp);
                        frameCount++;
                        if (frameCount == 1 || frameCount % 60 == 0)
                        {
                            // #region agent log
                            try
                            {
                                File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new
                                {
                                    location = "CanonEdsdkCameraService.cs:LiveViewLoop",
                                    message = "evf_frame_ready",
                                    data = new { frameCount },
                                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    sessionId = "debug-session",
                                    runId = "run1",
                                    hypothesisId = "H3"
                                }) + "\n");
                            }
                            catch { }
                            // #endregion
                        }
                    }
                    ReleaseQuietly(evfImage);
                    ReleaseQuietly(stream);
                    Thread.Sleep(30);
                }
                catch
                {
                    ReleaseQuietly(evfImage);
                    ReleaseQuietly(stream);
                    Thread.Sleep(100);
                }
            }
        }

        private static uint OnObjectEventStatic(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            System.Diagnostics.Debug.WriteLine($"[Shoot] OnObjectEventStatic 被呼叫 inEvent=0x{inEvent:X} inRef={inRef}");
            // #region agent log
            try
            {
                var line = JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:OnObjectEventStatic", message = "callback invoked", data = new { inEventHex = "0x" + inEvent.ToString("X"), inRef = inRef.ToString(), is208 = (inEvent == 0x208), is204 = (inEvent == 0x204) }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H1" }) + "\n";
                File.AppendAllText(DebugLogPath, line);
            }
            catch { }
            // #endregion
            var svc = CameraServiceProvider.Current as CanonEdsdkCameraService;
            return svc != null ? svc.OnObjectEventInstance(inEvent, inRef, inContext) : (uint)EdsError.OK;
        }

        /// <summary>Callback 在 EDSDK 背景執行緒執行。EdsRetain 後以 Dispatcher.InvokeAsync(Background) 排入 UI 執行緒下載，與 EdsInitializeSDK 同執行緒且不阻塞 UI。</summary>
        private uint OnObjectEventInstance(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            System.Diagnostics.Debug.WriteLine($"[Shoot] OnObjectEvent 收到 inEvent=0x{inEvent:X} inRef={inRef}");
            // 部分機型 (如 4000D) 可能送 0x204 (DirItemCreated) 或 0x208 (DirItemRequestTransfer)；皆可下載
            var isTransferEvent = (inEvent == (uint)EdsObjectEvent.DirItemRequestTransfer) || (inEvent == 0x204);
            if (!isTransferEvent || inRef == IntPtr.Zero)
            {
                // #region agent log
                try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:OnObjectEventInstance", message = "early_return", data = new { inEventHex = "0x" + inEvent.ToString("X"), inRefZero = (inRef == IntPtr.Zero), reason = inRef == IntPtr.Zero ? "inRefZero" : "eventNot208or204" }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H2" }) + "\n"); } catch { }
                // #endregion
                if (inRef != IntPtr.Zero) ReleaseQuietly(inRef);
                return (uint)EdsError.OK;
            }

            var retainResult = EdsRetain(inRef);
            // #region agent log
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:OnObjectEventInstance", message = "after_retain", data = new { retainResultHex = "0x" + retainResult.ToString("X"), ok = (retainResult == (uint)EdsError.OK) }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H3" }) + "\n"); } catch { }
            // #endregion
            if (retainResult != (uint)EdsError.OK)
            {
                // 部分機型 (如 4000D) 在 callback 執行緒上 EdsRetain 回傳 0x2，改為在當前執行緒直接下載，避免逾時
                System.Diagnostics.Debug.WriteLine($"[Shoot] OnObjectEvent: EdsRetain 失敗 (0x{retainResult:X})，改在 callback 執行緒直接下載");
                try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:OnObjectEventInstance", message = "download_in_callback", data = new { retainResultHex = "0x" + retainResult.ToString("X") }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H3" }) + "\n"); } catch { }
                _isDownloading = true;
                try
                {
                    var path = DownloadDirItemToPath(inRef);
                    _pendingCaptureTcs?.TrySetResult(path);
                    var disp = Application.Current?.Dispatcher;
                    if (disp != null)
                        disp.BeginInvoke(new Action(() => PhotoCaptured?.Invoke(this, path)), System.Windows.Threading.DispatcherPriority.Send);
                    else
                        PhotoCaptured?.Invoke(this, path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Shoot] OnObjectEvent 直接下載例外：{ex.GetType().Name} {ex.Message}");
                    try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:OnObjectEventInstance", message = "download_in_callback_exception", data = new { exType = ex.GetType().Name, exMessage = ex.Message }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H3" }) + "\n"); } catch { }
                    _pendingCaptureTcs?.TrySetException(ex);
                }
                finally
                {
                    ReleaseQuietly(inRef);
                    _isDownloading = false;
                }
                return (uint)EdsError.OK;
            }
            System.Diagnostics.Debug.WriteLine($"[Shoot] OnObjectEvent: 收到 inEvent=0x{inEvent:X}，委派至 UI 執行緒下載（DispatcherPriority.Send）…");
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:OnObjectEventInstance", message = "dispatcher_null", data = new { }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H3" }) + "\n"); } catch { }
                ReleaseQuietly(inRef);
                _pendingCaptureTcs?.TrySetException(new InvalidOperationException("No WPF Dispatcher for download."));
                return (uint)EdsError.OK;
            }
            _isDownloading = true;
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:OnObjectEventInstance", message = "invoke_async_scheduled", data = new { inRef = inRef.ToString() }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H4" }) + "\n"); } catch { }
            // 下載需盡快執行以避免相機撤銷傳輸；使用 Send 並同步 Invoke 強制序列化
            dispatcher.Invoke(() => DownloadAndRelease(inRef), System.Windows.Threading.DispatcherPriority.Send);
            return (uint)EdsError.OK;
        }

        /// <summary>共用下載邏輯：取得 DirItem 資訊、寫入檔案、DownloadComplete。不釋放 inRef，由呼叫端負責。</summary>
        private string DownloadDirItemToPath(IntPtr inRef)
        {
            var targetDir = !string.IsNullOrWhiteSpace(_pendingCaptureDir) ? _pendingCaptureDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhotoBooth", "Out");
            if (string.IsNullOrWhiteSpace(_pendingCaptureDir))
                System.Diagnostics.Debug.WriteLine($"[Shoot] DownloadDirItemToPath: _pendingCaptureDir 為空，改用 fallback {targetDir}");

            System.Diagnostics.Debug.WriteLine("[Download] 開始取得檔案資訊...");
            var infoErr = EdsGetDirectoryItemInfo(inRef, out var info);
            System.Diagnostics.Debug.WriteLine($"[Download] EdsGetDirectoryItemInfo 結果: 0x{infoErr:X}");
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadDirItemToPath", message = "EdsGetDirectoryItemInfo_result", data = new { errHex = "0x" + infoErr.ToString("X") }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H5" }) + "\n"); } catch { }
            EnsureSuccess(infoErr, "EdsGetDirectoryItemInfo");
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadDirItemToPath", message = "after_get_info", data = new { size = info.Size }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H5" }) + "\n"); } catch { }

            // 強制自訂檔名，避免 SDK 傳回的 szFileName 因 marshaling 錯位產生非法字元 (如 STX) 導致路徑無效
            var fileName = $"Capture_{_pendingCaptureIndex + 1}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            var path = Path.GetFullPath(Path.Combine(targetDir, fileName));
            System.Diagnostics.Debug.WriteLine($"[Download] 強制指定路徑: {path}");
            // #region agent log
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadDirItemToPath", message = "path_before_create", data = new { path, pathLen = path.Length, targetDirExists = Directory.Exists(targetDir), hypothesisId = "H1_path" }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1" }) + "\n"); } catch { }
            // #endregion
            try { Directory.CreateDirectory(Path.GetDirectoryName(path) ?? targetDir); } catch { }
            // 使用 EdsCreateFileStreamEx (Windows 為 WCHAR*) 確保 Unicode 路徑正確寫入磁碟；EdsCreateFileStream 為 ANSI 易導致檔案未建立
            var streamErr = EdsCreateFileStreamEx(path, (uint)EdsFileCreateDisposition.CreateAlways, (uint)EdsAccess.ReadWrite, out var stream);
            System.Diagnostics.Debug.WriteLine($"[Download] EdsCreateFileStreamEx 結果: 0x{streamErr:X}");
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadDirItemToPath", message = "EdsCreateFileStreamEx_result", data = new { errHex = "0x" + streamErr.ToString("X"), path, streamPtr = stream.ToString() }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H2_stream" }) + "\n"); } catch { }
            EnsureSuccess(streamErr, "EdsCreateFileStreamEx");

            var downloadErr = EdsDownload(inRef, (uint)Math.Min(info.Size, uint.MaxValue), stream);
            System.Diagnostics.Debug.WriteLine($"[Download] EdsDownload 結果: 0x{downloadErr:X}");
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadDirItemToPath", message = "EdsDownload_result", data = new { errHex = "0x" + downloadErr.ToString("X") }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H5" }) + "\n"); } catch { }
            EnsureSuccess(downloadErr, "EdsDownload");

            var completeErr = EdsDownloadComplete(inRef);
            System.Diagnostics.Debug.WriteLine($"[Download] EdsDownloadComplete 結果: 0x{completeErr:X}");
            EnsureSuccess(completeErr, "EdsDownloadComplete");
            ReleaseQuietly(stream);

            var exists = File.Exists(path);
            System.Diagnostics.Debug.WriteLine($"[Shoot] 相片已儲存：{path} File.Exists={exists}");
            // #region agent log
            string[] dirList = Array.Empty<string>();
            try { dirList = Directory.Exists(targetDir) ? Directory.GetFiles(targetDir) : Array.Empty<string>(); } catch { }
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadDirItemToPath", message = "after_release", data = new { path, fileExists = exists, targetDirFileCount = dirList.Length, targetDirFiles = Array.ConvertAll(dirList, Path.GetFileName) }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H3_after" }) + "\n"); } catch { }
            // #endregion
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadDirItemToPath", message = "before_try_set_result", data = new { path, pathLen = path?.Length ?? 0 }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H5" }) + "\n"); } catch { }
            return path;
        }

        /// <summary>在 UI 執行緒執行下載（與 EdsInitializeSDK 同執行緒），完成後 Release inRef。使用共用 DownloadDirItemToPath。</summary>
        private void DownloadAndRelease(IntPtr inRef)
        {
            System.Diagnostics.Debug.WriteLine("[Download] DownloadAndRelease 進入");
            try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadAndRelease", message = "entry", data = new { inRef = inRef.ToString() }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H4" }) + "\n"); } catch { }
            try
            {
                var path = DownloadDirItemToPath(inRef);
                _pendingCaptureTcs?.TrySetResult(path);
                PhotoCaptured?.Invoke(this, path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shoot] DownloadAndRelease 例外：{ex.GetType().Name} {ex.Message}");
                try { File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(new { location = "CanonEdsdkCameraService.cs:DownloadAndRelease", message = "exception", data = new { exType = ex.GetType().Name, exMessage = ex.Message }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H5" }) + "\n"); } catch { }
                _pendingCaptureTcs?.TrySetException(ex);
            }
            finally
            {
                ReleaseQuietly(inRef);
                _isDownloading = false;
                System.Diagnostics.Debug.WriteLine("[Download] 釋放物件完成。");
            }
        }

        public void Dispose()
        {
            try { StopLiveViewAsync().GetAwaiter().GetResult(); } catch { }
            lock (_sync)
            {
                if (_sessionOpen)
                {
                    try { EdsSendCommand(_camera, (uint)EdsCameraCommand.SetRemoteShootingMode, 0); } catch { }
                    try { EdsSendStatusCommand(_camera, (uint)EdsCameraStatusCommand.UIUnLock, 0); } catch { }
                    EdsCloseSession(_camera);
                    _sessionOpen = false;
                }
                ReleaseQuietly(_camera);
                ReleaseQuietly(_cameraList);
                if (_initialized)
                {
                    EdsTerminateSDK();
                    _initialized = false;
                }
            }
        }

        private static void ReleaseQuietly(IntPtr obj)
        {
            if (obj != IntPtr.Zero)
                EdsRelease(obj);
        }

        private static void EnsureSuccess(uint err, string operation)
        {
            if (err == (uint)EdsError.OK) return;
            throw new InvalidOperationException($"{operation} failed: 0x{err:X}");
        }

        private enum EdsError : uint
        {
            OK = 0x00000000,
            ObjectNotReady = 0x000000A1
        }

        private enum EdsSaveTo : uint
        {
            Host = 2
        }

        private enum EdsPropertyID : uint
        {
            SaveTo = 0x0000000B,
            EvfMode = 0x00000501,
            EvfOutputDevice = 0x00000500,
            EvfAFMode = 0x0000050E
        }

        /// <summary>EVF 對焦模式：Live = 連續對焦（主體移動會重新對焦），Quick = 單次對焦。</summary>
        private enum EdsEvfAFMode : uint
        {
            Quick = 0x00,
            Live = 0x01
        }

        private enum EdsEvfOutputDevice : uint
        {
            None = 0,
            PC = 2
        }

        private enum EdsCameraCommand : uint
        {
            TakePicture = 0x00000000,
            PressShutterButton = 0x00000004,
            DoEvfAf = 0x00000102,
            /// <summary>Live View 下驅動鏡頭對焦（與 kEdsCameraCommand_DriveLensEvf 相同）。</summary>
            DriveLensEvf = 0x00000103,
            SetRemoteShootingMode = 0x0000010f
        }

        /// <summary>EDSDK EdsEvfDriveLens（僅使用 Near1/Far1 計步；Far3 用於歸零）。</summary>
        private enum EdsEvfDriveLens : int
        {
            Near1 = 0x00000001,
            Far1 = 0x00008001,
            Far3 = 0x00008003
        }

        private enum EdsCameraStatusCommand : uint
        {
            UILock = 0x00000000,
            UIUnLock = 0x00000001
        }

        private enum EdsShutterButton : int
        {
            Off = 0x00000000,
            Halfway = 0x00000001,
            Completely = 0x00000003
        }

        private enum EdsEvfAf : int
        {
            Off = 0,
            On = 1
        }

        private enum EdsObjectEvent : uint
        {
            All = 0x00000200,
            /// <summary>影像可下載時由相機觸發，正確值為 0x208（原本誤植為 0x204 導致從未收到）</summary>
            DirItemRequestTransfer = 0x00000208
        }

        private enum EdsFileCreateDisposition : uint
        {
            CreateAlways = 1
        }

        private enum EdsAccess : uint
        {
            ReadWrite = 2
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EdsCapacity
        {
            public int NumberOfFreeClusters;
            public int BytesPerSector;
            public int Reset;
        }

        /// <summary>與 EDSDK tagEdsDirectoryItemInfo 對齊：size 為 8 位元組 (EdsUInt64)，否則 szFileName 會錯位出現 STX 等亂碼。</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct EdsDirectoryItemInfo
        {
            public long Size;
            public int IsFolder;
            public uint GroupID;
            public uint Option;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string FileName;
            public uint Format;
            public uint DateTime;
        }

        private delegate uint EdsObjectEventHandler(uint inEvent, IntPtr inRef, IntPtr inContext);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsInitializeSDK();

        [DllImport("EDSDK.dll")]
        private static extern uint EdsTerminateSDK();

        [DllImport("EDSDK.dll")]
        private static extern uint EdsGetCameraList(out IntPtr outCameraListRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsGetChildAtIndex(IntPtr inRef, int inIndex, out IntPtr outRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsOpenSession(IntPtr inCameraRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsCloseSession(IntPtr inCameraRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsRelease(IntPtr inRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsRetain(IntPtr inRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsSendStatusCommand(IntPtr inCameraRef, uint inStatusCommand, int inParam);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsSetObjectEventHandler(
            IntPtr inCameraRef,
            uint inEvent,
            EdsObjectEventHandler inEventHandler,
            IntPtr inContext);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsSetPropertyData(
            IntPtr inRef,
            uint inPropertyID,
            int inParam,
            int inSize,
            ref uint inData);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsSetCapacity(IntPtr inCameraRef, EdsCapacity inCapacity);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsSendCommand(IntPtr inCameraRef, uint inCommand, int inParam);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsGetDirectoryItemInfo(IntPtr inDirItemRef, out EdsDirectoryItemInfo outDirItemInfo);

        [DllImport("EDSDK.dll", CharSet = CharSet.Ansi)]
        private static extern uint EdsCreateFileStream(
            string inFileName,
            uint inCreateDisposition,
            uint inDesiredAccess,
            out IntPtr outStream);

        /// <summary>Windows 上接受 Unicode (WCHAR*)，應用於含非 ASCII 或長路徑的檔名。</summary>
        [DllImport("EDSDK.dll", CharSet = CharSet.Unicode, EntryPoint = "EdsCreateFileStreamEx")]
        private static extern uint EdsCreateFileStreamEx(
            string inFileName,
            uint inCreateDisposition,
            uint inDesiredAccess,
            out IntPtr outStream);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsDownload(IntPtr inDirItemRef, uint inReadSize, IntPtr outStream);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsDownloadComplete(IntPtr inDirItemRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsCreateMemoryStream(uint inBufferSize, out IntPtr outStream);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsCreateEvfImageRef(IntPtr inStreamRef, out IntPtr outEvfImageRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsDownloadEvfImage(IntPtr inCameraRef, IntPtr outEvfImageRef);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsGetLength(IntPtr inStreamRef, out uint outLength);

        [DllImport("EDSDK.dll")]
        private static extern uint EdsGetPointer(IntPtr inStreamRef, out IntPtr outPointer);

        /// <summary>讓 SDK 處理來自相機的訊息（如 DirItemRequestTransfer），必須在等待照片的迴圈中呼叫，否則背景執行緒收不到 OnObjectEvent。</summary>
        [DllImport("EDSDK.dll")]
        private static extern uint EdsGetEvent();
    }
}

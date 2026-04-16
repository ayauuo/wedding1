using Microsoft.Web.WebView2.Core;
using PhotoBoothWin.Bridge;
using PhotoBoothWin.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PhotoBoothWin
{
    public partial class MainWindow : Window
    {
        private readonly BoothBridge _bridge = new BoothBridge();
        private RS232BillAcceptor? _billAcceptor;
        private ArduinoCoinAcceptor? _coinAcceptor;
        private bool _paymentsEnabled = true;
        private DateTime _lastLiveViewPost = DateTime.MinValue;
        private const int LiveViewThrottleMs = 100;
        private int _liveViewFramesPushed;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += async (_, __) =>
            {
                await Web.EnsureCoreWebView2Async();

                // 關閉追蹤防護，避免 CDN (如 glfx.js) 的 storage 存取被擋
                Web.CoreWebView2.Profile.PreferredTrackingPreventionLevel =
                    Microsoft.Web.WebView2.Core.CoreWebView2TrackingPreventionLevel.None;

                // 禁用 WebView2 的右鍵選單
                Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                Web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

                Web.CoreWebView2.PermissionRequested += (s, e) =>
                {
                    if (e.PermissionKind == CoreWebView2PermissionKind.Camera)
                    {
                        e.State = CoreWebView2PermissionState.Allow;
                        e.Handled = true;
                    }
                };

                // 前端資料夾：可透過環境變數 PHOTOBOOTH_WEB_ROOT 或 exe 旁 web_root.txt（第一行路徑）覆寫，否則用 exe 旁 web 資料夾
                var webRoot = GetWebRootPath();
                System.Diagnostics.Debug.WriteLine($"[WebView] 前端路徑: {webRoot}");
                var webExists = System.IO.Directory.Exists(webRoot);
                var indexPath = System.IO.Path.Combine(webRoot, "index.html");
                var indexExists = System.IO.File.Exists(indexPath);
                System.Diagnostics.Debug.WriteLine($"[WebView] web 資料夾存在: {webExists}, index.html 存在: {indexExists}");
                if (!webExists || !indexExists)
                    System.Diagnostics.Debug.WriteLine($"[WebView] 空白畫面可能原因：前端檔案未複製到輸出目錄，請確認建置後 bin 內有 web\\index.html");
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app", webRoot, CoreWebView2HostResourceAccessKind.Allow);

                Web.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    var uri = (s as Microsoft.Web.WebView2.Core.CoreWebView2)?.Source ?? "";
                    System.Diagnostics.Debug.WriteLine($"[WebView] NavigationCompleted IsSuccess={e.IsSuccess}, WebErrorStatus={e.WebErrorStatus}, Uri={uri}");
                    if (!e.IsSuccess)
                        System.Diagnostics.Debug.WriteLine($"[WebView] 導航失敗，可能導致空白畫面");
                };
                // 拍照輸出資料夾：與 BoothBridge.CaptureOutputDirectory 一致，Vue 用 https://photos/檔名 載入
                var photosDir = BoothBridge.CaptureOutputDirectory;
                try
                {
                    System.IO.Directory.CreateDirectory(photosDir);
                    System.Diagnostics.Debug.WriteLine($"[WebView] 拍照儲存／讀取路徑: {photosDir}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebView] 建立拍照資料夾失敗 {photosDir}: {ex.Message}");
                }
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "photos", photosDir, CoreWebView2HostResourceAccessKind.Allow);

                // EDSDK Live View 幀推送到 WebView（Vue 顯示在網頁上）
                var cameraService = CameraServiceProvider.Current;
                cameraService.LiveViewFrameReady += OnLiveViewFrameReady;

                Web.CoreWebView2.WebMessageReceived += async (s, e) =>
                {
                    var msg = e.TryGetWebMessageAsString();
                    var webView = Web.CoreWebView2;

                    // 處理紙鈔機控制消息
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(msg);
                        if (jsonDoc.RootElement.TryGetProperty("@event", out var eventProp))
                        {
                            var eventName = eventProp.GetString();
                            if (eventName == "bill_acceptor_control")
                            {
                                if (jsonDoc.RootElement.TryGetProperty("enabled", out var enabledProp))
                                {
                                    bool enabled = enabledProp.GetBoolean();
                                    HandleBillAcceptorControl(enabled);
                                    return; // 不轉發給 Bridge
                                }
                            }
                            if (eventName == "set_payments_enabled")
                            {
                                if (jsonDoc.RootElement.TryGetProperty("enabled", out var enabledProp))
                                {
                                    _paymentsEnabled = enabledProp.GetBoolean();
                                    HandleBillAcceptorControl(_paymentsEnabled);
                                    System.Diagnostics.Debug.WriteLine(_paymentsEnabled ? "✓ 收錢已啟用" : "✓ 收錢已暫停（上傳中）");
                                    return;
                                }
                            }
                            if (eventName == "payments_config")
                            {
                                bool billEnabled = true;
                                bool coinEnabled = true;
                                if (jsonDoc.RootElement.TryGetProperty("billAcceptorEnabled", out var billProp))
                                    billEnabled = billProp.GetBoolean();
                                if (jsonDoc.RootElement.TryGetProperty("coinAcceptorEnabled", out var coinProp))
                                    coinEnabled = coinProp.GetBoolean();
                                System.Diagnostics.Debug.WriteLine($"✓ 收到 payments_config：紙鈔機={billEnabled}, 投幣器={coinEnabled}");
                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(5000);
                                    Dispatcher.Invoke(() =>
                                    {
                                        if (coinEnabled) StartCoinAcceptor();
                                        if (billEnabled) StartBillAcceptor();
                                        if (!billEnabled && !coinEnabled)
                                            System.Diagnostics.Debug.WriteLine("紙鈔機與投幣器皆已關閉，點擊螢幕即可進入選版型");
                                    });
                                });
                                return;
                            }
                        }
                    }
                    catch
                    {
                        // 如果不是 JSON 或格式不對，繼續正常處理
                    }

                    // 其他消息轉發給 Bridge（含 upload / upload_video 由 C# 上傳到網頁）
                    // #region agent log
                    if (msg != null && msg.IndexOf("load_captures", StringComparison.Ordinal) >= 0) { try { var lp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log"); System.IO.File.AppendAllText(lp, System.Text.Json.JsonSerializer.Serialize(new { location = "MainWindow.WebMessageReceived:before_HandleAsync_load_captures", message = "load_captures RPC received", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H4" }) + "\n"); } catch { } }
                    // #endregion
                    var resp = await _bridge.HandleAsync(msg);
                    // #region agent log
                    if (msg != null && msg.IndexOf("load_captures", StringComparison.Ordinal) >= 0) { try { var lp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log"); System.IO.File.AppendAllText(lp, System.Text.Json.JsonSerializer.Serialize(new { location = "MainWindow.WebMessageReceived:after_HandleAsync_load_captures", message = "load_captures HandleAsync returned", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H4" }) + "\n"); } catch { } }
                    // #endregion
                    if (resp != null && webView != null)
                        webView.PostWebMessageAsString(resp);
                };

                Web.Source = new Uri("https://app/index.html");

                BoothBridge.OpenWpfShootRequested = () => Dispatcher.Invoke(ShowWpfShoot);
                BoothBridge.ReturnToWebViewRequested = () => Dispatcher.Invoke(HideWpfShoot);
                BoothBridge.ReturnToWebAndStartSynthesisRequested = () => Dispatcher.Invoke(ReturnToWebAndStartSynthesis);

                // WebView 加載完成；紙鈔機／投幣器由 Vue 發送 payments_config 後依 .env 開關啟動
                Web.CoreWebView2.DOMContentLoaded += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine("✓ WebView DOM 已加載完成（等待 Vue 發送 payments_config）");
                };
            };
        }

        private void StartBillAcceptor()
        {
            // 使用我們自己的驅動程式，不需要檢查外部程式
            Task.Run(async () =>
            {
                int maxRetries = 3;
                int retryDelay = 2000; // 每次重試間隔 2 秒
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // 列出所有可用的串口
                        string[] ports = System.IO.Ports.SerialPort.GetPortNames();
                        System.Diagnostics.Debug.WriteLine($"=== RS232 紙鈔機初始化 (嘗試 {attempt}/{maxRetries}) ===");
                        System.Diagnostics.Debug.WriteLine($"系統中可用的串口：{string.Join(", ", ports)}");
                        
                        // 如果沒有找到 COM 口，嘗試檢測並自動安裝驅動
                        if (ports.Length == 0 && attempt == 1)
                        {
                            System.Diagnostics.Debug.WriteLine("⚠ 未找到任何 COM 口，可能是驅動未安裝");
                            
                            // 獲取診斷信息
                            var diagnosticInfo = GetDriverDiagnosticInfo();
                            
                            if (diagnosticInfo.hasUnrecognizedDevices)
                            {
                                System.Diagnostics.Debug.WriteLine("發現未識別的 USB 設備，嘗試通過 Windows Update 自動搜索驅動...");
                                bool driverInstalled = await SerialPortDriverHelper.TryAutoInstallDriverViaWindowsUpdate();
                                
                                if (driverInstalled)
                                {
                                    // 重新檢查 COM 口
                                    ports = System.IO.Ports.SerialPort.GetPortNames();
                                    System.Diagnostics.Debug.WriteLine($"驅動安裝成功，找到 {ports.Length} 個 COM 口");
                                }
                            }
                        }
                        
                        // 自動偵測串口
                        string? detectedPort = AutoDetectBillAcceptorPort();
                        string portToUse = detectedPort ?? "COM7"; // 默認使用 COM7
                        System.Diagnostics.Debug.WriteLine($"使用串口：{portToUse}");
                        
                        // 創建 RS232 監聽服務
                        _billAcceptor = new RS232BillAcceptor(portToUse, 9600);
                        
                        // 訂閱所有事件
                        _billAcceptor.BillReceived += OnBillReceived;
                        _billAcceptor.StatusChanged += OnStatusChanged;
                        _billAcceptor.ErrorOccurred += OnErrorOccurred;
                        System.Diagnostics.Debug.WriteLine("✓ 已訂閱所有事件");
                        
                        // 開始監聽
                        _billAcceptor.Start();
                        
                        // 等待一小段時間，讓串口有時間打開
                        await Task.Delay(500);
                        
                        // 檢查是否成功開啟
                        if (_billAcceptor != null && _billAcceptor.IsOpen)
                        {
                            System.Diagnostics.Debug.WriteLine("✓ RS232 紙鈔機監聽服務已成功啟動");
                            System.Diagnostics.Debug.WriteLine("等待接收紙鈔機訊號...");
                            System.Diagnostics.Debug.WriteLine("提示：詳細日誌已保存到 Logs 資料夾");
                            
                            // 在 UI 上顯示成功訊息
                            Dispatcher.Invoke(() =>
                            {
                                ShowStatusMessage($"紙鈔機已連接 ({portToUse})", isError: false);
                            });
                            
                            return; // 成功，退出重試循環
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"✗ 嘗試 {attempt}/{maxRetries}：串口未能成功打開");
                            Dispatcher.Invoke(() =>
                            {
                                ShowStatusMessage($"串口連接失敗（嘗試 {attempt}/{maxRetries}）", isError: true);
                            });
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        string errorMsg = $"串口訪問被拒絕：{ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"✗ 嘗試 {attempt}/{maxRetries}：{errorMsg}");
                        
                        Dispatcher.Invoke(() =>
                        {
                            ShowStatusMessage($"權限錯誤：請以管理員身份運行程式", isError: true);
                        });
                        
                        if (attempt < maxRetries)
                        {
                            System.Diagnostics.Debug.WriteLine($"  等待 {retryDelay / 1000} 秒後重試...");
                            await Task.Delay(retryDelay);
                        }
                        else
                        {
                            // 最後一次重試失敗，只記錄日誌
                            System.Diagnostics.Debug.WriteLine($"✗ 已重試 {maxRetries} 次，仍然無法打開串口");
                            Dispatcher.Invoke(() =>
                            {
                                ShowStatusMessage("紙鈔機連接失敗，請查看日誌", isError: true);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"啟動紙鈔機失敗：{ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"✗ {errorMsg}");
                        System.Diagnostics.Debug.WriteLine($"  詳細錯誤：{ex}");
                        
                        Dispatcher.Invoke(() =>
                        {
                            ShowStatusMessage("紙鈔機初始化失敗，請查看日誌", isError: true);
                        });
                        
                        break; // 其他錯誤不重試
                    }
                }
            });
        }

        /// <summary>
        /// 啟動 Arduino 投幣器（COM8, 115200）。收到 PULSES=50 時會觸發 50 元付款事件。
        /// </summary>
        private void StartCoinAcceptor()
        {
            Task.Run(() =>
            {
                try
                {
                    string[] ports = System.IO.Ports.SerialPort.GetPortNames();
                    System.Diagnostics.Debug.WriteLine($"[投幣器] 可用串口：{string.Join(", ", ports)}");
                    if (!ports.Any(p => string.Equals(p, "COM8", StringComparison.OrdinalIgnoreCase)))
                    {
                        System.Diagnostics.Debug.WriteLine("[投幣器] 未找到 COM8，跳過 Arduino 投幣器（請確認投幣器接在 COM8）");
                        return;
                    }
                    _coinAcceptor = new ArduinoCoinAcceptor("COM8", 115200);
                    _coinAcceptor.CoinReceived += OnBillReceived; // 與紙鈔共用同一付款處理
                    _coinAcceptor.StatusChanged += (s, msg) => System.Diagnostics.Debug.WriteLine($"[投幣器] {msg}");
                    _coinAcceptor.ErrorOccurred += (s, err) => System.Diagnostics.Debug.WriteLine($"[投幣器錯誤] {err}");
                    _coinAcceptor.Start();
                    if (_coinAcceptor.IsOpen)
                    {
                        System.Diagnostics.Debug.WriteLine("✓ Arduino 投幣器已連接 (COM8)，等待 PULSES=數字 格式資料");
                        Dispatcher.Invoke(() => ShowStatusMessage("投幣器已連接 (COM8)", isError: false));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[投幣器] COM8 開啟失敗（可能已被其他程式占用）");
                        NotifyCoinAcceptorPortInUse();
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[投幣器] COM8 被占用或無權限：{ex.Message}");
                    NotifyCoinAcceptorPortInUse();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[投幣器] 啟動失敗：{ex.Message}");
                }
            });
        }

        /// <summary>
        /// 取得前端（web）資料夾路徑。可透過環境變數 PHOTOBOOTH_WEB_ROOT 或 exe 旁 web_root.txt（第一行）覆寫，否則為 exe 旁 web 資料夾。
        /// </summary>
        private static string GetWebRootPath()
        {
            var fromEnv = Environment.GetEnvironmentVariable("PHOTOBOOTH_WEB_ROOT");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                var path = fromEnv.Trim();
                if (Directory.Exists(path)) return Path.GetFullPath(path);
            }
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web_root.txt");
                if (File.Exists(configPath))
                {
                    var line = File.ReadAllLines(configPath).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(line) && Directory.Exists(line))
                        return Path.GetFullPath(line);
                }
            }
            catch { /* 沿用預設 */ }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
        }

        /// <summary>
        /// COM8 被占用時，在 UI 與 WebView 顯示解決方式。
        /// </summary>
        private void NotifyCoinAcceptorPortInUse()
        {
            const string msg = "COM8 被占用，請關閉 RS232-ICT004.exe 或其他使用 COM8 的程式";
            Dispatcher.Invoke(() =>
            {
                ShowStatusMessage(msg, isError: true);
                try
                {
                    if (Web.CoreWebView2 != null)
                    {
                        var json = JsonSerializer.Serialize(new { @event = "status", message = msg, isError = true });
                        Web.CoreWebView2.PostWebMessageAsString(json);
                    }
                }
                catch { }
            });
        }

        private void OnBillReceived(object? sender, int amount)
        {
            if (!_paymentsEnabled) return;
            System.Diagnostics.Debug.WriteLine($"=== 收到付款事件：{amount} 元 ===");
            
            // 使用 UI 線程發送訊息到 WebView
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (Web.CoreWebView2 != null)
                    {
                        // 發送事件訊息到 WebView，更新計數器
                        var message = JsonSerializer.Serialize(new { @event = "paid", amount = amount });
                        System.Diagnostics.Debug.WriteLine($"準備發送消息到 WebView：{message}");
                        Web.CoreWebView2.PostWebMessageAsString(message);
                        System.Diagnostics.Debug.WriteLine($"✓ 已發送付款事件到 WebView：{amount} 元");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("✗ WebView.CoreWebView2 為 null，無法發送消息");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"✗ 發送付款事件失敗：{ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"  詳細錯誤：{ex}");
                }
            });
        }

        /// <summary>
        /// 自動偵測紙鈔機串口
        /// 支援多種 COM 口格式，包括 "COM1", "COM2" 等標準格式
        /// </summary>
        private string? AutoDetectBillAcceptorPort()
        {
            try
            {
                string[] ports = System.IO.Ports.SerialPort.GetPortNames();
                System.Diagnostics.Debug.WriteLine($"系統中可用的串口：{string.Join(", ", ports)}");
                
                if (ports.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("警告：系統中沒有可用的串口");
                    return null;
                }
                
                // 優先順序：COM7（紙鈔機專用），COM1、COM2、COM4…；COM8 保留給 Arduino 投幣器
                string[] preferredPorts = { "COM7", "COM1", "COM2", "COM4", "COM5", "COM6", "COM8", "COM9", "COM10" };
                
                // 首先嘗試找到優先串口（精確匹配）
                foreach (string preferred in preferredPorts)
                {
                    if (ports.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"找到優先串口：{preferred}");
                        return preferred;
                    }
                }
                
                // 如果沒有找到優先串口，嘗試從所有串口中找到第一個有效的 COM 口
                // COM8 保留給 Arduino 投幣器，紙鈔機不可使用
                var validComPorts = ports
                    .Where(p => p.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p, "COM8", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => 
                    {
                        // 提取 COM 後的數字進行排序
                        if (p.Length > 3 && int.TryParse(p.Substring(3), out int num))
                            return num;
                        return int.MaxValue;
                    })
                    .ToList();
                
                if (validComPorts.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"找到有效串口（已排除 COM8）：{string.Join(", ", validComPorts)}");
                    System.Diagnostics.Debug.WriteLine($"使用第一個有效串口：{validComPorts[0]}");
                    return validComPorts[0];
                }
                
                // 若僅剩 COM8（已保留給投幣器），紙鈔機無可用串口
                System.Diagnostics.Debug.WriteLine("沒有紙鈔機可用串口（COM8 已保留給投幣器）");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"自動偵測串口失敗：{ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 處理狀態變化事件
        /// </summary>
        private void OnStatusChanged(object? sender, string status)
        {
            System.Diagnostics.Debug.WriteLine($"[狀態] {status}");
            // 可以通過 WebView 發送消息到前端顯示
            try
            {
                if (Web.CoreWebView2 != null)
                {
                    var statusMsg = JsonSerializer.Serialize(new 
                    { 
                        @event = "status", 
                        message = status, 
                        isError = false 
                    });
                    Web.CoreWebView2.PostWebMessageAsString(statusMsg);
                }
            }
            catch
            {
                // 如果發送失敗，靜默處理
            }
        }
        
        /// <summary>
        /// 處理錯誤事件
        /// </summary>
        private void OnErrorOccurred(object? sender, string error)
        {
            System.Diagnostics.Debug.WriteLine($"[錯誤] {error}");
            Dispatcher.Invoke(() =>
            {
                ShowStatusMessage(error, isError: true);
            });
        }
        
        /// <summary>
        /// 處理紙鈔機控制消息（啟用/禁用）
        /// </summary>
        private void HandleBillAcceptorControl(bool enabled)
        {
            try
            {
                if (_billAcceptor == null || !_billAcceptor.IsOpen)
                {
                    System.Diagnostics.Debug.WriteLine($"紙鈔機未連接，無法{(enabled ? "啟用" : "禁用")}");
                    return;
                }
                
                if (enabled)
                {
                    _billAcceptor.EnableValidator();
                    System.Diagnostics.Debug.WriteLine("✓ 紙鈔機已啟用（回到待機畫面）");
                }
                else
                {
                    _billAcceptor.DisableValidator();
                    System.Diagnostics.Debug.WriteLine("✓ 紙鈔機已禁用（進入選擇版型頁面）");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 控制紙鈔機失敗：{ex.Message}");
            }
        }
        
        /// <summary>
        /// 顯示狀態訊息（可以擴展為在 UI 上顯示）
        /// </summary>
        private void ShowStatusMessage(string message, bool isError)
        {
            System.Diagnostics.Debug.WriteLine($"{(isError ? "[錯誤]" : "[狀態]")} {message}");
            
            // 可以通過 WebView 發送消息到前端顯示
            try
            {
                if (Web.CoreWebView2 != null)
                {
                    var statusMsg = JsonSerializer.Serialize(new 
                    { 
                        @event = "status", 
                        message = message, 
                        isError = isError 
                    });
                    Web.CoreWebView2.PostWebMessageAsString(statusMsg);
                }
            }
            catch
            {
                // 如果發送失敗，靜默處理
            }
        }

        /// <summary>
        /// 獲取驅動診斷信息
        /// </summary>
        private (bool hasUnrecognizedDevices, string? chipType, int comPortCount) GetDriverDiagnosticInfo()
        {
            try
            {
                // 檢查未識別的 USB 設備
                var unrecognizedDevices = SerialPortDriverHelper.GetUnrecognizedUsbDevices();
                bool hasUnrecognized = unrecognizedDevices != null && unrecognizedDevices.Length > 0;
                
                // 檢測晶片類型
                string? chipType = SerialPortDriverHelper.DetectUsbSerialChipType();
                
                // 檢查 COM 口數量
                var ports = System.IO.Ports.SerialPort.GetPortNames();
                int comPortCount = ports.Length;
                
                System.Diagnostics.Debug.WriteLine($"診斷信息：");
                System.Diagnostics.Debug.WriteLine($"  - 未識別的 USB 設備：{hasUnrecognized}");
                System.Diagnostics.Debug.WriteLine($"  - 檢測到的晶片類型：{chipType ?? "未知"}");
                System.Diagnostics.Debug.WriteLine($"  - COM 口數量：{comPortCount}");
                
                return (hasUnrecognized, chipType, comPortCount);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"獲取診斷信息失敗：{ex.Message}");
                return (false, null, 0);
            }
        }
        

        private void OnLiveViewFrameReady(object? sender, System.Windows.Media.Imaging.BitmapSource frame)
        {
            if (!BoothBridge.LiveViewPushToWeb || frame == null)
            {
                if (!BoothBridge.LiveViewPushToWeb)
                {
                    var dropNow = DateTime.Now;
                    if ((dropNow - _lastLiveViewPost).TotalSeconds > 2)
                    {
                        // #region agent log
                        try
                        {
                            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log");
                            var line = System.Text.Json.JsonSerializer.Serialize(new { location = "MainWindow.xaml.cs:OnLiveViewFrameReady", message = "frame_dropped_push_disabled", data = new { }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "H2" }) + "\n";
                            System.IO.File.AppendAllText(logPath, line);
                        }
                        catch { }
                        // #endregion
                    }
                }
                return;
            }
            var now = DateTime.Now;
            if ((now - _lastLiveViewPost).TotalMilliseconds < LiveViewThrottleMs) return;
            // 若超過 2 秒沒推送過，視為新一輪 Live View，重設幀計數以便除錯日誌再印「第一幀」
            if ((now - _lastLiveViewPost).TotalSeconds > 2)
                _liveViewFramesPushed = 0;
            _lastLiveViewPost = now;

            Dispatcher.InvokeAsync(() =>
            {
                if (!BoothBridge.LiveViewPushToWeb) return;
                try
                {
                    using var mem = new MemoryStream();
                    var encoder = new JpegBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                    encoder.Save(mem);
                    mem.Position = 0;
                    var bytes = mem.ToArray();
                    var base64 = Convert.ToBase64String(bytes);
                    var dataUrl = "data:image/jpeg;base64," + base64;
                    var json = JsonSerializer.Serialize(new { @event = "liveview_frame", dataUrl });
                    Web.CoreWebView2?.PostWebMessageAsString(json);
                    _liveViewFramesPushed++;
                    if (_liveViewFramesPushed == 1)
                        System.Diagnostics.Debug.WriteLine("[Live View] 第一幀已推送到 WebView。");
                    else if (_liveViewFramesPushed % 60 == 0)
                        System.Diagnostics.Debug.WriteLine($"[Live View] 已推送 {_liveViewFramesPushed} 幀到 WebView。");
                }
                catch
                {
                    // 單幀編碼失敗不影響後續
                }
            });
        }

        private void ShowWpfShoot()
        {
            BoothBridge.IsWpfShootEmbedded = true;
            WebViewPanel.Visibility = Visibility.Collapsed;
            WpfShootPanel.Visibility = Visibility.Visible;
            WpfFrame.Navigate(new PhotoBoothWin.Pages.TemplatePage());
        }

        private void HideWpfShoot()
        {
            BoothBridge.IsWpfShootEmbedded = false;
            WpfShootPanel.Visibility = Visibility.Collapsed;
            WebViewPanel.Visibility = Visibility.Visible;
        }

        /// <summary>WPF 拍完四張並選濾鏡後按「下一步」：回到 WebView，通知 Vue 用 load_captures 取圖並執行合成/上傳/QR。</summary>
        private void ReturnToWebAndStartSynthesis()
        {
            // #region agent log
            try { var lp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log"); System.IO.File.AppendAllText(lp, System.Text.Json.JsonSerializer.Serialize(new { location = "MainWindow.ReturnToWebAndStartSynthesis:entry", message = "entry", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H5" }) + "\n"); } catch { }
            // #endregion
            var vueTemplateId = BoothBridge.GetVueTemplateIdForSynthesis();
            WpfShootPanel.Visibility = Visibility.Collapsed;
            WebViewPanel.Visibility = Visibility.Visible;
            try
            {
                var msg = JsonSerializer.Serialize(new { @event = "wpf_shoot_done", templateId = vueTemplateId });
                Web.CoreWebView2?.PostWebMessageAsString(msg);
                // #region agent log
                try { var lp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log"); System.IO.File.AppendAllText(lp, System.Text.Json.JsonSerializer.Serialize(new { location = "MainWindow.ReturnToWebAndStartSynthesis:after_post", message = "PostWebMessageAsString done", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H5" }) + "\n"); } catch { }
                // #endregion
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WPF] 通知 Vue 合成失敗: {ex.Message}");
            }
        }

        private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 禁用右鍵選單
            e.Handled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            BoothBridge.LiveViewPushToWeb = false;
            _liveViewFramesPushed = 0;
            CameraServiceProvider.Current.LiveViewFrameReady -= OnLiveViewFrameReady;
            _billAcceptor?.Dispose();
            _coinAcceptor?.Dispose();
            base.OnClosed(e);
        }
    }
}

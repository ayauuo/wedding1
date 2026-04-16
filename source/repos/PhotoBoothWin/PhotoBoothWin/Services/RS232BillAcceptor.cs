using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;

namespace PhotoBoothWin.Services
{
    /// <summary>
    /// RS232-ICT004 紙鈔機串口監聽服務
    /// 使用完整的 ICT 協議實現，支援 Escrow 模式和狀態碼處理
    /// </summary>
    public class RS232BillAcceptor : IDisposable
    {
        private SerialPort? _serialPort;
        private bool _disposed = false;
        private string _portName;
        private int _baudRate;
        private readonly Queue<byte> _receiveBuffer = new Queue<byte>(); // 使用 Queue 管理接收緩衝區
        private ReceiveState _currentState = ReceiveState.Idle; // 狀態機
        private bool _isEscrowMode = false; // Escrow 模式標記
        private DateTime _escrowStartTime; // Escrow 開始時間
        
        // 幣值映射表（支援多種編碼格式）
        private readonly Dictionary<byte, int> _denominationMap = new Dictionary<byte, int>
        {
            { 0x10, 100 },    // 100元 (十六進制)
            { 0x20, 200 },    // 200元
            { 0x50, 500 },    // 500元
            { 0x64, 100 },    // 100元 (十進制)
            { 0xC8, 200 },    // 200元 (十進制)
            { 0x40, 100 },    // ICT 協議幣值 1
            { 0x41, 200 },    // ICT 協議幣值 2
            { 0x42, 500 },    // ICT 協議幣值 3
            { 0x43, 1000 },   // ICT 協議幣值 4
            { 0x44, 2000 },   // ICT 協議幣值 5
            { 0x31, 100 },    // ASCII '1' → 100
            { 0x32, 200 },    // ASCII '2' → 200
            { 0x35, 500 },    // ASCII '5' → 500
            { 0x00, 100 },    // 0x00 可能表示 100 元
            { 0x01, 100 },
            { 0x02, 200 },
            { 0x05, 500 },
            { 0x0A, 1000 },
            { 0x11, 100 },
            { 0x12, 200 },
            { 0x15, 500 },
            { 0x1A, 1000 },
            { 0x14, 2000 },
            { 0x24, 2000 },
            { 0xF4, 500 },   // 可能的 500 元編碼
            { 0x68, 100 },   // 部分機型 100 元代碼
            { 0xAE, 100 },   // 部分機型 100 元代碼（常見於台幣辨識）
            { 0x6C, 100 }    // 部分機型 100 元（與 0x68 相近）
        };
        
        public event EventHandler<int>? BillReceived; // 參數是金額（元）
        public event EventHandler<string>? StatusChanged; // 狀態變化事件
        public event EventHandler<string>? ErrorOccurred; // 錯誤事件
        
        /// <summary>
        /// 接收狀態枚舉（狀態機模式）
        /// </summary>
        private enum ReceiveState
        {
            Idle,                    // 空閒狀態
            WaitingForDenomination   // 等待面額數據
        }

        /// <summary>
        /// 檢查串口是否已打開
        /// </summary>
        public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

        public RS232BillAcceptor(string portName = "COM1", int baudRate = 9600)
        {
            _portName = portName;
            _baudRate = baudRate;
        }

        /// <summary>
        /// 開始監聽串口
        /// </summary>
        public void Start()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                System.Diagnostics.Debug.WriteLine("RS232 串口已經在運行");
                return; // 已經在運行
            }

            try
            {
                // 列出所有可用的串口
                string[] availablePorts = SerialPort.GetPortNames();
                System.Diagnostics.Debug.WriteLine($"可用的串口：{string.Join(", ", availablePorts)}");
                
                // 嘗試找到可用的串口（COM8 保留給 Arduino 投幣器，紙鈔機不可使用）
                if (!TryFindPort(out string? foundPort))
                {
                    System.Diagnostics.Debug.WriteLine($"警告：找不到指定的串口 {_portName}，嘗試使用其他可用串口（排除 COM8）");
                    string? fallback = availablePorts.FirstOrDefault(p =>
                        !string.Equals(p, "COM8", StringComparison.OrdinalIgnoreCase));
                    if (fallback != null)
                    {
                        foundPort = fallback;
                        System.Diagnostics.Debug.WriteLine($"使用串口：{foundPort}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("錯誤：沒有紙鈔機可用串口（僅 COM8 時保留給投幣器）");
                        return;
                    }
                }

                // 檢查串口是否被占用
                if (foundPort != null && IsPortInUse(foundPort))
                {
                    System.Diagnostics.Debug.WriteLine($"警告：串口 {foundPort} 可能被其他程式占用（例如 RS232-ICT004.exe）");
                    System.Diagnostics.Debug.WriteLine("提示：如果 RS232-ICT004.exe 正在運行，請關閉它或使用不同的串口");
                }

                // 根據 RS232-ICT004 規格：
                // - 數據格式：1 起始位、8 數據位、偶校驗（Even）、1 停止位
                // - 波特率：9600 bps
                _serialPort = new SerialPort(foundPort, _baudRate, Parity.Even, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000,
                    DtrEnable = true,  // 啟用 DTR（某些設備需要）
                    RtsEnable = true   // 啟用 RTS（某些設備需要）
                };

                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                
                _serialPort.Open();
                LogMessage($"✓ RS232 串口已成功開啟：{foundPort}，波特率：{_baudRate}，校驗位：Even");
                LogMessage($"  串口狀態 - IsOpen: {_serialPort.IsOpen}, DtrEnable: {_serialPort.DtrEnable}, RtsEnable: {_serialPort.RtsEnable}");
                
                // 等待串口穩定
                System.Threading.Thread.Sleep(200);
                
                // 根據 ICT 協議，發送啟用指令 [0x3E] 來啟用紙鈔機
                // 注意：ICT 協議是事件驅動的，不需要輪詢機制
                EnableValidator();
                
                StatusChanged?.Invoke(this, "紙鈔機初始化完成，等待紙鈔...");
                LogMessage("✓ 紙鈔機已啟用，等待接收紙鈔...");
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 串口訪問被拒絕：{ex.Message}");
                System.Diagnostics.Debug.WriteLine($"  可能原因：串口被其他程式占用（例如 RS232-ICT004.exe）");
                System.Diagnostics.Debug.WriteLine($"  解決方案：");
                System.Diagnostics.Debug.WriteLine($"    1. 關閉 RS232-ICT004.exe 程式");
                System.Diagnostics.Debug.WriteLine($"    2. 或使用不同的串口");
                System.Diagnostics.Debug.WriteLine($"    3. 或修改 RS232-ICT004.exe 的串口設定");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 開啟串口失敗：{ex.Message}");
                System.Diagnostics.Debug.WriteLine($"  詳細錯誤：{ex}");
            }
        }

        /// <summary>
        /// 檢查串口是否被占用
        /// </summary>
        private bool IsPortInUse(string portName)
        {
            try
            {
                using (var testPort = new SerialPort(portName))
                {
                    testPort.Open();
                    testPort.Close();
                    return false; // 可以打開，表示未被占用
                }
            }
            catch
            {
                return true; // 無法打開，可能被占用
            }
        }

        /// <summary>
        /// 嘗試找到可用的串口（COM8 保留給投幣器，紙鈔機不使用）
        /// </summary>
        private bool TryFindPort(out string? portName)
        {
            portName = null;
            string[] ports = SerialPort.GetPortNames();
            
            if (ports.Length == 0)
            {
                return false;
            }

            // 優先使用指定的端口（且非 COM8）
            foreach (string port in ports)
            {
                if (port.Equals(_portName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(port, "COM8", StringComparison.OrdinalIgnoreCase))
                {
                    portName = port;
                    return true;
                }
            }

            // 若指定埠不存在或為 COM8，使用第一個「非 COM8」的埠
            string? fallback = ports.FirstOrDefault(p =>
                !string.Equals(p, "COM8", StringComparison.OrdinalIgnoreCase));
            if (fallback != null)
            {
                portName = fallback;
                return true;
            }
            return false;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                LogMessage("警告：串口未開啟，無法讀取數據");
                return;
            }

            try
            {
                // 讀取所有可用數據
                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead == 0)
                {
                    return;
                }

                byte[] buffer = new byte[bytesToRead];
                int bytesRead = _serialPort.Read(buffer, 0, bytesToRead);

                if (bytesRead > 0)
                {
                    // 將數據添加到緩衝區
                    foreach (byte b in buffer)
                    {
                        _receiveBuffer.Enqueue(b);
                    }
                    
                    // 處理緩衝區中的數據
                    ProcessBuffer();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"✗ 讀取串口數據失敗：{ex.Message}");
                ErrorOccurred?.Invoke(this, $"讀取串口數據失敗：{ex.Message}");
            }
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            LogMessage($"✗ 串口錯誤：{e.EventType}");
            ErrorOccurred?.Invoke(this, $"串口錯誤：{e.EventType}");
        }

        /// <summary>
        /// 處理接收緩衝區中的數據（使用狀態機和完整的 ICT 協議）
        /// </summary>
        private void ProcessBuffer()
        {
            // 檢查 Escrow 超時（5秒）
            if (_isEscrowMode && (DateTime.Now - _escrowStartTime).TotalSeconds > 5)
            {
                LogMessage("⚠ Escrow 超時，紙鈔被自動拒收");
                ErrorOccurred?.Invoke(this, "Escrow 超時，紙鈔被自動拒收");
                _isEscrowMode = false;
                _currentState = ReceiveState.Idle;
            }
            
            while (_receiveBuffer.Count > 0)
            {
                byte currentByte = _receiveBuffer.Dequeue();
                LogMessage($"[接收] 0x{currentByte:X2}");
                
                // 根據 ICT 協議處理每個字節
                ProcessByteICTProtocol(currentByte);
            }
        }
        
        /// <summary>
        /// 根據 ICT 協議處理每個字節（核心協議處理邏輯）
        /// </summary>
        private void ProcessByteICTProtocol(byte data)
        {
            switch (data)
            {
                // 1. 上電訊號（必須在2秒內回應）
                case 0x80:
                case 0x8F:
                    LogMessage("✓ 收到紙鈔機上電訊號");
                    StatusChanged?.Invoke(this, "紙鈔機上電，發送回應...");
                    SendCommand(new byte[] { 0x02 });  // 回應上電
                    _currentState = ReceiveState.Idle;
                    break;
                    
                // 2. 紙鈔驗證成功（進入 Escrow 模式）
                case 0x81:
                    LogMessage("✓ 收到紙鈔驗證成功訊號 [0x81]");
                    StatusChanged?.Invoke(this, "紙鈔驗證成功，等待幣值...");
                    _isEscrowMode = true;
                    _escrowStartTime = DateTime.Now;
                    _currentState = ReceiveState.WaitingForDenomination;
                    break;
                    
                // 3. 幣值代碼（在 Escrow 模式下處理）
                // 注意：0x20 可能是幣值（200元）或狀態碼，需要根據上下文判斷
                case 0x10:
                case 0x30:
                case 0x40:
                case 0x41:
                case 0x42:
                case 0x43:
                case 0x44:
                case 0x50:
                case 0x64:
                case 0xC8:
                case 0x31:
                case 0x32:
                case 0x35:
                case 0x68:   // 部分機型 100 元
                case 0xAE:   // 部分機型 100 元（台幣常見）
                case 0x6C:   // 部分機型 100 元
                    if (_isEscrowMode || _currentState == ReceiveState.WaitingForDenomination)
                    {
                        ProcessBillDenomination(data);
                        _isEscrowMode = false;
                        _currentState = ReceiveState.Idle;
                    }
                    else
                    {
                        // 非 Escrow 模式下收到幣值，可能是直接入鈔模式
                        ProcessBillDenomination(data);
                    }
                    break;
                    
                // 特殊處理：0x20 可能是幣值（200元）或狀態碼（重啟）
                case 0x20:
                    if (_isEscrowMode || _currentState == ReceiveState.WaitingForDenomination)
                    {
                        // 在等待幣值時，0x20 視為幣值（200元）
                        ProcessBillDenomination(data);
                        _isEscrowMode = false;
                        _currentState = ReceiveState.Idle;
                    }
                    else
                    {
                        // 否則視為狀態碼
                        StatusChanged?.Invoke(this, "重啟紙鈔機");
                    }
                    break;
                    
                // 4. 狀態碼處理
                case 0x21:
                    LogMessage("✗ 馬達故障");
                    ErrorOccurred?.Invoke(this, "馬達故障");
                    break;
                case 0x22:
                    LogMessage("✗ 校驗和錯誤");
                    ErrorOccurred?.Invoke(this, "校驗和錯誤");
                    break;
                case 0x23:
                    LogMessage("✗ 卡鈔！請檢查紙鈔機");
                    ErrorOccurred?.Invoke(this, "卡鈔！請檢查紙鈔機");
                    break;
                case 0x24:
                    StatusChanged?.Invoke(this, "紙鈔被取走");
                    break;
                case 0x25:
                    LogMessage("✗ 錢箱未關閉");
                    ErrorOccurred?.Invoke(this, "錢箱未關閉");
                    break;
                case 0x27:
                    LogMessage("✗ 感應器問題");
                    ErrorOccurred?.Invoke(this, "感應器問題");
                    break;
                case 0x28:
                    LogMessage("✗ 鉤鈔（Bill Fish）");
                    ErrorOccurred?.Invoke(this, "鉤鈔（Bill Fish）");
                    break;
                case 0x29:
                    LogMessage("✗ 錢箱問題");
                    ErrorOccurred?.Invoke(this, "錢箱問題");
                    break;
                case 0x2A:
                    StatusChanged?.Invoke(this, "紙鈔被拒收");
                    break;
                case 0x2E:
                    LogMessage("✗ 無效指令");
                    ErrorOccurred?.Invoke(this, "無效指令");
                    break;
                case 0x2F:
                    StatusChanged?.Invoke(this, "保留狀態");
                    break;
                case 0x3E:
                    StatusChanged?.Invoke(this, "紙鈔機啟用狀態");
                    break;
                case 0x5E:
                    StatusChanged?.Invoke(this, "紙鈔機禁用狀態");
                    break;
                    
                // 5. 狀態／回應字節（不觸發付款，僅略過，避免「未知字節」日誌）
                case 0x71:
                    break;
                case 0xFC:
                case 0xF8:
                case 0xF6:
                case 0xE0:
                    // 常見為狀態／ACK，不記錄為未知
                    break;

                default:
                    // 未知字節，如果在 Escrow 模式下，嘗試解析為面額
                    if (_isEscrowMode || _currentState == ReceiveState.WaitingForDenomination)
                    {
                        int amount = ParseBillAmount(data);
                        if (amount > 0)
                        {
                            ProcessBillDenomination(data);
                            _isEscrowMode = false;
                            _currentState = ReceiveState.Idle;
                        }
                        else
                        {
                            LogMessage($"⚠ 未知字節在 Escrow 模式下：0x{data:X2}");
                        }
                    }
                    else
                    {
                        LogMessage($"⚠ 未知字節：0x{data:X2}");
                    }
                    break;
            }
        }
        
        /// <summary>
        /// 處理幣值數據
        /// 只接受 100 元紙鈔，其他面額將被拒收
        /// </summary>
        private void ProcessBillDenomination(byte billCode)
        {
            int amount = ParseBillAmount(billCode);
            
            if (amount > 0)
            {
                LogMessage($"✓ 解析成功：收到紙鈔 {amount} 元 (原始數據: 0x{billCode:X2})");
                
                // 只接受 100 元紙鈔
                if (amount == 100)
                {
                    StatusChanged?.Invoke(this, $"收到紙鈔: {amount}元");
                    
                    // 觸發紙鈔接收事件
                    if (BillReceived != null)
                    {
                        BillReceived.Invoke(this, amount);
                        LogMessage($"✓ BillReceived 事件已觸發：{amount} 元");
                    }
                    else
                    {
                        LogMessage("✗ 警告：BillReceived 事件沒有訂閱者！");
                    }
                    
                    // 根據 ICT 協議，收到幣值後需要回應接受指令 [0x02]
                    SendCommand(new byte[] { 0x02 });
                    LogMessage("  已發送接受訊號 [0x02]");
                }
                else
                {
                    // 不是 100 元，拒收紙鈔
                    LogMessage($"✗ 拒收紙鈔：只接受 100 元，收到 {amount} 元");
                    StatusChanged?.Invoke(this, $"拒收紙鈔：只接受 100 元，收到 {amount} 元");
                    ErrorOccurred?.Invoke(this, $"拒收紙鈔：只接受 100 元，收到 {amount} 元");
                    
                    // 不發送接受指令（0x02），紙鈔機會自動拒收紙鈔
                    // 根據 ICT 協議，在 Escrow 模式下，如果不發送接受指令，紙鈔機會自動退還紙鈔
                    LogMessage("  未發送接受訊號，紙鈔機將自動退還紙鈔");
                }
            }
            else
            {
                LogMessage($"✗ 無法解析的面額代碼: 0x{billCode:X2}");
                ErrorOccurred?.Invoke(this, $"無法解析的面額代碼: 0x{billCode:X2}");
                
                // 無法解析的面額也不接受
                LogMessage("  未發送接受訊號，紙鈔機將自動退還紙鈔");
            }
        }
        
        /// <summary>
        /// 啟用紙鈔機（發送 0x3E）
        /// </summary>
        public void EnableValidator()
        {
            SendCommand(new byte[] { 0x3E });
            StatusChanged?.Invoke(this, "紙鈔機已啟用");
            LogMessage("✓ 已發送啟用指令 [0x3E]");
        }
        
        /// <summary>
        /// 禁用紙鈔機（發送 0x5E）
        /// </summary>
        public void DisableValidator()
        {
            SendCommand(new byte[] { 0x5E });
            StatusChanged?.Invoke(this, "紙鈔機已禁用");
            LogMessage("✓ 已發送禁用指令 [0x5E]");
        }
        
        /// <summary>
        /// 重置紙鈔機（發送 0x30）
        /// </summary>
        public void ResetValidator()
        {
            SendCommand(new byte[] { 0x30 });
            StatusChanged?.Invoke(this, "紙鈔機重置中...");
            LogMessage("✓ 已發送重置指令 [0x30]");
            
            // 重置後紙鈔機會發送上電訊號，需要回應
            System.Threading.Thread.Sleep(100);
        }
        
        /// <summary>
        /// 發送指令到紙鈔機
        /// </summary>
        private void SendCommand(byte[] command)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Write(command, 0, command.Length);
                    _serialPort.BaseStream.Flush();
                    
                    string hexString = string.Join(" ", Array.ConvertAll(command, b => $"0x{b:X2}"));
                    LogMessage($"[發送] {hexString}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"✗ 發送指令失敗：{ex.Message}");
                ErrorOccurred?.Invoke(this, $"發送指令失敗：{ex.Message}");
            }
        }
        
        /// <summary>
        /// 記錄日誌訊息（帶時間戳，同時輸出到 Debug 和文件）
        /// </summary>
        private void LogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] {message}";
            
            // 輸出到 Debug（開發時可見）
            System.Diagnostics.Debug.WriteLine(logEntry);
            
            // 同時寫入文件日誌（部署後可見）
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, $"BillAcceptor_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logFile, logEntry + Environment.NewLine);
            }
            catch
            {
                // 如果寫入文件失敗，靜默處理（避免影響主要功能）
            }
        }
        

        /// <summary>
        /// 解析紙鈔面額（使用映射表和多種解析方式）
        /// </summary>
        private int ParseBillAmount(byte billCode)
        {
            // 優先使用映射表
            if (_denominationMap.ContainsKey(billCode))
            {
                return _denominationMap[billCode];
            }
            
            // 嘗試其他解析方式
            
            // 1. ASCII 數字字符（'0'-'9'）
            if (billCode >= 0x30 && billCode <= 0x39)
            {
                int digit = billCode - 0x30;
                return digit switch
                {
                    1 => 100,
                    2 => 200,
                    5 => 500,
                    0 => 1000,
                    _ => digit * 100
                };
            }
            
            // 2. 嘗試十六進制直接轉換（某些設備可能使用）
            // 注意：這是一個備用方案，可能需要根據實際設備調整
            if (billCode >= 0x10 && billCode <= 0x99)
            {
                // 某些設備可能使用比例轉換
                // 例如：0x10 = 16 → 16 * 6.25 = 100
                // 但這需要根據實際設備驗證，暫時不使用
            }
            
            return 0;
        }

        /// <summary>
        /// 停止監聽
        /// </summary>
        public void Stop()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                // 禁用紙鈔機
                DisableValidator();
                System.Threading.Thread.Sleep(100);
                
                _serialPort.Close();
                LogMessage("✓ RS232 串口已關閉");
                StatusChanged?.Invoke(this, "紙鈔機已停止");
            }
            
            // 重置狀態
            _currentState = ReceiveState.Idle;
            _isEscrowMode = false;
            _receiveBuffer.Clear();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _serialPort?.Dispose();
                _disposed = true;
            }
        }
    }
}

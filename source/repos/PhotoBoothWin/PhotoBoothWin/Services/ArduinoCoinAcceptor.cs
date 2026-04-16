using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace PhotoBoothWin.Services
{
    /// <summary>
    /// Arduino 投幣器串口監聽服務。
    /// 透過 COM 口接收文字協議，例如 PULSES=50 表示收到 50 元（1 pulse = 1 元）。
    /// 預設 COM8、115200 baud。
    /// 支援 JY-616 等脈衝型投幣器：JY-616 為脈衝輸出，需經 Arduino 讀取 COIN 腳脈衝後
    /// 由 Arduino 以串口送出（例如 PULSES=10 表示 10 元、或純數字 10）。
    /// </summary>
    public class ArduinoCoinAcceptor : IDisposable
    {
        private SerialPort? _serialPort;
        private bool _disposed;
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly StringBuilder _lineBuffer = new StringBuilder();

        /// <summary>收到投幣時觸發，參數為金額（元）。</summary>
        public event EventHandler<int>? CoinReceived;

        /// <summary>狀態或日誌訊息。</summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>錯誤時觸發。</summary>
        public event EventHandler<string>? ErrorOccurred;

        public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

        public ArduinoCoinAcceptor(string portName = "COM8", int baudRate = 115200)
        {
            _portName = portName;
            _baudRate = baudRate;
        }

        /// <summary>
        /// 開始監聽串口，讀取文字行並解析 PULSES=數字（視為金額）。
        /// </summary>
        public void Start()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                Log("投幣器串口已在運行");
                return;
            }

            try
            {
                string[] ports = SerialPort.GetPortNames();
                Log($"可用串口：{string.Join(", ", ports)}");

                string? portToUse = null;
                foreach (string p in ports)
                {
                    if (string.Equals(p, _portName, StringComparison.OrdinalIgnoreCase))
                    {
                        portToUse = p;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(portToUse) && ports.Length > 0)
                    portToUse = ports[0];

                if (string.IsNullOrEmpty(portToUse))
                {
                    Log("錯誤：沒有可用的 COM 口");
                    ErrorOccurred?.Invoke(this, "沒有可用的 COM 口");
                    return;
                }

                // Arduino 常用：8N1、無 Handshake
                _serialPort = new SerialPort(portToUse, _baudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000,
                    Encoding = Encoding.UTF8,
                    NewLine = "\n"
                };

                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                _serialPort.Open();

                Log($"投幣器串口已開啟：{portToUse}，{_baudRate} baud");
                StatusChanged?.Invoke(this, "投幣器已連接，等待投幣...");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log($"串口訪問被拒絕：{ex.Message}");
                ErrorOccurred?.Invoke(this, $"串口訪問被拒絕：{ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"開啟投幣器串口失敗：{ex.Message}");
                ErrorOccurred?.Invoke(this, $"開啟投幣器串口失敗：{ex.Message}");
            }
        }

        private void SerialPort_DataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                int toRead = _serialPort.BytesToRead;
                if (toRead == 0) return;

                byte[] buf = new byte[toRead];
                int read = _serialPort.Read(buf, 0, toRead);
                if (read <= 0) return;

                string chunk = Encoding.UTF8.GetString(buf, 0, read);
                _lineBuffer.Append(chunk);

                ProcessLines();
            }
            catch (Exception ex)
            {
                Log($"讀取投幣器數據失敗：{ex.Message}");
                ErrorOccurred?.Invoke(this, $"讀取投幣器數據失敗：{ex.Message}");
            }
        }

        private void SerialPort_ErrorReceived(object? sender, SerialErrorReceivedEventArgs e)
        {
            Log($"串口錯誤：{e.EventType}");
            ErrorOccurred?.Invoke(this, $"串口錯誤：{e.EventType}");
        }

        /// <summary>
        /// 從緩衝區取出完整行，解析 PULSES=數字，數字即為金額（元）。
        /// 支援以 \n 或 \r 或 \r\n 結尾的資料行。
        /// </summary>
        private void ProcessLines()
        {
            string content = _lineBuffer.ToString();
            int lastN = content.LastIndexOf('\n');
            int lastR = content.LastIndexOf('\r');
            int lineEnd = Math.Max(lastN, lastR);
            if (lineEnd < 0) return;

            string fullLines = content.Substring(0, lineEnd + 1);
            _lineBuffer.Clear();
            _lineBuffer.Append(content.Substring(lineEnd + 1));

            foreach (string line in fullLines.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                Log($"[投幣器] {trimmed}");

                int amount = 0;

                // 1. PULSES=50 → 50 元（1 pulse = 1 元，JY-616 常用格式）
                var matchPulses = Regex.Match(trimmed, @"PULSES\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                if (matchPulses.Success && int.TryParse(matchPulses.Groups[1].Value, out int pulses) && pulses > 0)
                {
                    amount = pulses;
                }
                // 2. COIN=10 或 coin=10（部分 Arduino 範例格式）
                else if (Regex.Match(trimmed, @"COIN\s*=\s*(\d+)", RegexOptions.IgnoreCase) is { Success: true } mCoin
                    && int.TryParse(mCoin.Groups[1].Value, out int coinVal) && coinVal > 0)
                {
                    amount = coinVal;
                }
                // 3. 純數字一行（例如 10、50），視為金額（元）
                else if (Regex.IsMatch(trimmed, @"^\d+$") && int.TryParse(trimmed, out int numVal) && numVal > 0)
                {
                    amount = numVal;
                }

                if (amount > 0)
                {
                    Log($"收到投幣：{amount} 元");
                    StatusChanged?.Invoke(this, $"收到投幣：{amount} 元");
                    CoinReceived?.Invoke(this, amount);
                }
            }
        }

        public void Stop()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                Log("投幣器串口已關閉");
                StatusChanged?.Invoke(this, "投幣器已停止");
            }
            _lineBuffer.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _serialPort?.Dispose();
            _disposed = true;
        }

        private void Log(string message)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ArduinoCoin] {message}";
            System.Diagnostics.Debug.WriteLine(entry);
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, $"CoinAcceptor_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logFile, entry + Environment.NewLine);
            }
            catch { }
        }
    }
}

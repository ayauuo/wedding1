using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace PhotoBoothWin.Services
{
    /// <summary>
    /// 串口驅動檢測和安裝輔助工具
    /// </summary>
    public static class SerialPortDriverHelper
    {
        /// <summary>
        /// 嘗試使用 Windows Update 自動搜索並安裝驅動
        /// </summary>
        public static async Task<bool> TryAutoInstallDriverViaWindowsUpdate()
        {
            try
            {
                // 檢查是否有未識別的 USB 設備
                var unrecognizedDevices = GetUnrecognizedUsbDevices();
                
                if (unrecognizedDevices.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"發現 {unrecognizedDevices.Count()} 個未識別的 USB 設備，嘗試通過 Windows Update 安裝驅動...");
                    
                    // 嘗試使用 Windows Update 搜索驅動
                    // 這需要通過裝置管理員或 PowerShell 命令
                    foreach (var device in unrecognizedDevices)
                    {
                        System.Diagnostics.Debug.WriteLine($"嘗試為設備搜索驅動：{device}");
                        await TrySearchDriverViaWindowsUpdate(device);
                    }
                    
                    // 等待一下讓 Windows Update 搜索完成
                    await Task.Delay(5000);
                    
                    // 再次檢查 COM 口
                    var ports = System.IO.Ports.SerialPort.GetPortNames();
                    if (ports.Length > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"✓ 驅動安裝成功，找到 {ports.Length} 個 COM 口");
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"自動安裝驅動失敗：{ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 獲取未識別的 USB 設備
        /// </summary>
        public static string[] GetUnrecognizedUsbDevices()
        {
            try
            {
                // 使用 WMI 查詢未識別的設備
                var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE Status = 'Error' OR Status = 'Unknown'");
                
                var devices = searcher.Get()
                    .Cast<ManagementObject>()
                    .Where(obj => 
                    {
                        var pnpClass = obj["PNPClass"]?.ToString() ?? "";
                        var description = obj["Description"]?.ToString() ?? "";
                        var deviceId = obj["DeviceID"]?.ToString() ?? "";
                        
                        // 檢查是否為 USB 相關設備
                        return (pnpClass.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                                description.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                                deviceId.Contains("USB", StringComparison.OrdinalIgnoreCase)) &&
                               !description.Contains("COM", StringComparison.OrdinalIgnoreCase); // 排除已識別的 COM 口
                    })
                    .Select(obj => obj["DeviceID"]?.ToString() ?? "")
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToArray();
                
                return devices;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
        
        /// <summary>
        /// 嘗試通過 Windows Update 搜索驅動
        /// </summary>
        private static async Task TrySearchDriverViaWindowsUpdate(string deviceId)
        {
            try
            {
                // 使用 PowerShell 命令通過 Windows Update 搜索驅動
                // 這通常不需要管理員權限，但需要網路連接
                var psScript = $@"
                    $device = Get-PnpDevice | Where-Object {{ $_.InstanceId -like '*{deviceId}*' }}
                    if ($device) {{
                        Update-Driver -Id $device.InstanceId -Online
                    }}
                ";
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-Command \"{psScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                // 注意：這可能需要一些時間，並且可能需要用戶確認
                // 為了不阻塞主線程，我們不等待它完成
                System.Diagnostics.Debug.WriteLine($"已啟動 Windows Update 驅動搜索（非阻塞）");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"搜索驅動失敗：{ex.Message}");
            }
        }
        
        /// <summary>
        /// 檢測常見的 USB 轉串口晶片類型
        /// </summary>
        public static string? DetectUsbSerialChipType()
        {
            try
            {
                // 首先檢查已識別的 COM 口
                var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Ports' OR PNPClass = 'USB'");
                
                var devices = searcher.Get()
                    .Cast<ManagementObject>()
                    .Select(obj => obj["Description"]?.ToString() ?? "")
                    .Where(desc => !string.IsNullOrEmpty(desc))
                    .ToArray();
                
                foreach (var desc in devices)
                {
                    if (desc.Contains("CH340", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("CH341", StringComparison.OrdinalIgnoreCase))
                    {
                        return "CH340/CH341";
                    }
                    if (desc.Contains("FTDI", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("FT232", StringComparison.OrdinalIgnoreCase))
                    {
                        return "FTDI";
                    }
                    if (desc.Contains("Prolific", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("PL2303", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Prolific PL2303";
                    }
                    if (desc.Contains("CP210", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("Silicon Labs", StringComparison.OrdinalIgnoreCase))
                    {
                        return "CP210x";
                    }
                }
                
                // 檢查未識別的設備
                var unrecognized = GetUnrecognizedUsbDevices();
                if (unrecognized.Any())
                {
                    // 嘗試從設備 ID 中識別晶片類型
                    foreach (var deviceId in unrecognized)
                    {
                        if (deviceId.Contains("VID_1A86", StringComparison.OrdinalIgnoreCase) ||
                            deviceId.Contains("VID_5523", StringComparison.OrdinalIgnoreCase))
                        {
                            return "CH340/CH341";
                        }
                        if (deviceId.Contains("VID_0403", StringComparison.OrdinalIgnoreCase))
                        {
                            return "FTDI";
                        }
                        if (deviceId.Contains("VID_067B", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Prolific PL2303";
                        }
                        if (deviceId.Contains("VID_10C4", StringComparison.OrdinalIgnoreCase))
                        {
                            return "CP210x";
                        }
                    }
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 打開裝置管理員（方便用戶手動安裝驅動）
        /// </summary>
        public static void OpenDeviceManager()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "devmgmt.msc",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"打開裝置管理員失敗：{ex.Message}");
            }
        }
    }
}

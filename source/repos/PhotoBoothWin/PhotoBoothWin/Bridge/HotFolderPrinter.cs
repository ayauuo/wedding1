using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PhotoBoothWin.Services
{
    public static class HotFolderPrinter
    {
        private const string DefaultHotRoot = @"C:\DNP\Hot Folder\Prints";
        private const string DefaultHotFolderExe = @"C:\DNP\Hot Folder\Hot Folder.exe";

        /// <summary>本地列印輸出目錄（Prints），可透過環境變數或 hot_folder_path.txt 覆寫。</summary>
        public static string HotRoot => GetHotFolderSetting(0, "PHOTOBOOTH_PRINTS_PATH", DefaultHotRoot);

        /// <summary>Hot Folder.exe 路徑，可透過環境變數或 hot_folder_path.txt 覆寫。</summary>
        public static string HotFolderExe => GetHotFolderSetting(1, "DNP_HOT_FOLDER_EXE", DefaultHotFolderExe);

        private static string GetHotFolderSetting(int lineIndex, string envVar, string fallback)
        {
            var fromEnv = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return fromEnv.Trim();

            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hot_folder_path.txt");
                if (File.Exists(configPath))
                {
                    var lines = File.ReadAllLines(configPath);
                    if (lineIndex < lines.Length && !string.IsNullOrWhiteSpace(lines[lineIndex]))
                        return lines[lineIndex].Trim();
                }
            }
            catch { /* 沿用預設 */ }

            return fallback;
        }

        /// <summary>
        /// 確保 Hot Folder.exe 正在運行，如果沒有則啟動它
        /// </summary>
        private static void EnsureHotFolderRunning()
        {
            var exePath = HotFolderExe;
            // 檢查程序是否已經在運行
            var processName = Path.GetFileNameWithoutExtension(exePath);
            var isRunning = Process.GetProcessesByName(processName).Any();

            if (isRunning)
            {
                return; // 已經在運行，不需要啟動
            }

            // 檢查執行檔是否存在
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException($"找不到 Hot Folder 執行檔: {exePath}");
            }

            // 啟動程序
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process.Start(startInfo);
                
                // 等待一下讓程序啟動
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                throw new Exception($"無法啟動 Hot Folder.exe: {ex.Message}", ex);
            }
        }

        public static void SendToHotFolder(string finalImagePath, string sizeKey, int copies)
        {
            if (string.IsNullOrWhiteSpace(finalImagePath) || !File.Exists(finalImagePath))
                throw new FileNotFoundException("找不到要列印的檔案", finalImagePath);

            // 確保 Hot Folder.exe 正在運行
            EnsureHotFolderRunning();

            copies = Math.Clamp(copies, 1, 5);

            var root = HotRoot;
            var folder = Path.Combine(root, sizeKey);   // ex: 4x6 / 2x6 / 4x6_2IN / 3_5x5
            Directory.CreateDirectory(folder);

            for (int i = 1; i <= copies; i++)
            {
                var name = $"job_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{i}.jpg";
                var tmp = Path.Combine(folder, name + ".tmp");
                var dst = Path.Combine(folder, name);

                File.Copy(finalImagePath, tmp, overwrite: true);
                File.Move(tmp, dst);

                // 保險：避免同毫秒檔名撞到、也讓 HFP 讀取更穩
                Thread.Sleep(25);
            }
        }
    }
}

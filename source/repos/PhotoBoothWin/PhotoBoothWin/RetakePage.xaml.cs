using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Threading.Tasks;

namespace PhotoBoothWin.Pages
{
    public partial class RetakePage : Page
    {
        private readonly int _index;
        private MediaPlayer? _countdownPlayer; // 倒數音效播放器

        public RetakePage(int index)
        {
            InitializeComponent();
            _index = index;
            Loaded += async (_, __) => await DoRetakeAsync();
        }

        /// <summary>
        /// 播放倒數音效
        /// </summary>
        private void PlayCountdownSound()
        {
            try
            {
                var soundPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "web", "assets", "templates", "music", "倒數10秒拍照.mp3");

                if (File.Exists(soundPath))
                {
                    _countdownPlayer?.Stop();
                    _countdownPlayer?.Close();
                    _countdownPlayer = new MediaPlayer();
                    _countdownPlayer.Open(new Uri(soundPath));
                    _countdownPlayer.Play();
                }
            }
            catch
            {
                // 如果播放失敗，靜默處理
            }
        }

        private async Task DoRetakeAsync()
        {
            var s = BoothStore.Current;

            HintText.Text = $"準備重拍第 {_index + 1} 張";

            PlayCountdownSound(); // 播放倒數音效
            await Task.Delay(2000); // 倒數前多停1秒
            for (int sec = 10; sec >= 1; sec--)
            {
                CountdownText.Text = sec.ToString();
                await Task.Delay(1000); // 每個數字間隔1秒
            }

            CountdownText.Text = "📸";
            HintText.Text = $"重拍第 {_index + 1} 張…";

            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "PhotoBoothWin");
            Directory.CreateDirectory(dir);

            var path = System.IO.Path.Combine(dir, $"retake_{DateTime.Now:yyyyMMdd_HHmmss}_{_index + 1}.png");
            FakeCamera.SaveFakeShot(path, _index); // 之後換真相機只改這行
            s.ShotPaths[_index] = path;

            // 這裡是關鍵：標記「這張已重拍過一次」
            s.Retaken[_index] = true;

            NavigationService?.Navigate(new PreviewPage());
        }
    }
}

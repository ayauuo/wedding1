using PhotoBoothWin.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;


namespace PhotoBoothWin.Pages
{
    public partial class CapturePage : Page
    {
        private CancellationTokenSource? _cts;
        private readonly ICameraService _cameraService = CameraServiceProvider.Current;

        public CapturePage()
        {
            InitializeComponent();
            Loaded += async (_, __) => await StartSequenceAsync();
            Unloaded += (_, __) => _cts?.Cancel();
        }

        private async Task StartSequenceAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var s = BoothStore.Current;
            TitleText.Text = $"版型：{s.TemplateId}｜拍攝中…";

            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "PhotoBoothWin");
            Directory.CreateDirectory(dir);
            CameraCaptureStore.Clear();
            await _cameraService.InitializeAsync();

            for (int i = 0; i < 4; i++)
            {
                // 每張拍之前倒數10秒
                for (int sec = 10; sec >= 1; sec--)
                {
                    CountdownText.Text = sec.ToString();
                    TitleText.Text = $"版型：{s.TemplateId}｜準備拍第 {i + 1} 張";
                    await Task.Delay(1000, ct);
                }

                CountdownText.Text = "📸";
                TitleText.Text = $"版型：{s.TemplateId}｜拍第 {i + 1} 張…";

                var path = await CapturePhotoAsync(dir, i);
                s.ShotPaths[i] = path;
                CameraCaptureStore.SetCapture(i, path);

                SetThumb(i, path);
                await Task.Delay(300, ct);
            }

            NavigationService?.Navigate(new PreviewPage());
        }

        private void SetThumb(int idx, string path)
        {
            var bmp = new BitmapImage(new Uri(path));
            if (idx == 0) Img1.Source = bmp;
            if (idx == 1) Img2.Source = bmp;
            if (idx == 2) Img3.Source = bmp;
            if (idx == 3) Img4.Source = bmp;
        }

        private async Task<string> CapturePhotoAsync(string dir, int index)
        {
            try
            {
                await _cameraService.HalfPressShutterAsync();
                await Task.Delay(300).ConfigureAwait(false);
                return await _cameraService.TakePictureAsync(dir, index);
            }
            catch
            {
                var path = System.IO.Path.Combine(dir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}_{index + 1}.png");
                FakeCamera.SaveFakeShot(path, index);
                return path;
            }
        }
    }
}



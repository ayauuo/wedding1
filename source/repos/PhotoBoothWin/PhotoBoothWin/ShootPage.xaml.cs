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

using PhotoBoothWin.Services;
using PhotoBoothWin.ViewModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace PhotoBoothWin.Pages
{
    public partial class ShootPage : Page
    {
        private readonly ShootVm vm = new ShootVm();
        private readonly ICameraService _cameraService = CameraServiceProvider.Current;
        private bool _busy = false;          // 拍攝/重拍時鎖UI
        private int _shotCount = 0;          // 已拍張數 0..4
        private readonly string _dir;
        private MediaPlayer? _countdownPlayer; // 倒數音效播放器
        private bool _liveViewHooked = false;

        // 你準備的 4 個拍攝外框圖片（隨拍攝次數切換）
        private readonly string[] BigFrameFiles =
        {
            "Assets/Frames/big_1.png",
            "Assets/Frames/big_2.png",
            "Assets/Frames/big_3.png",
            "Assets/Frames/big_4.png",
        };

        // 縮圖框（可用同一張或不同張）
        private readonly string[] ThumbFrameFiles =
        {
            "Assets/Frames/thumb_1.png",
            "Assets/Frames/thumb_2.png",
            "Assets/Frames/thumb_3.png",
            "Assets/Frames/thumb_4.png",
        };

        public ShootPage()
        {
            InitializeComponent();
            DataContext = vm;

            _dir = PhotoBoothWin.Bridge.BoothBridge.CaptureOutputDirectory;
            Directory.CreateDirectory(_dir);

            Loaded += async (_, __) =>
            {
                await InitializeCameraAsync();
                await StartAutoShootAsync();
            };
            Unloaded += async (_, __) => await _cameraService.StopLiveViewAsync();
        }

        // 進入此畫面：隱藏濾鏡/重拍/下一步，直接開始拍4張（每張前倒數10）
        private async Task StartAutoShootAsync()
        {
            SetModeShooting();
            _shotCount = 0;
            CameraCaptureStore.Clear();

            // 載入框圖
            for (int i = 0; i < 4; i++)
            {
                vm.ThumbFrameSources[i] = TryLoadImg(ThumbFrameFiles[i]);
                vm.ShotImageSources[i] = null;
                vm.Retaken[i] = false;
                vm.PhotoFilters[i] = "";
                vm.IsThumbSelected[i] = false;
            }

            vm.SelectedIndex = 0;
            vm.IsThumbSelected[0] = true;
            ApplyBigViewForShootingFrame(0);

            vm.NotifyAll();

            // 拍四張：每張倒數期間持續觸發 EVF 對焦（RunContinuousEvfAfAsync），倒數結束後半按鎖焦再拍照
            for (int i = 0; i < 4; i++)
            {
                await CountdownAndCaptureIntoIndex(i);
            }

            SetModeReview();
            SelectThumb(0);
        }

        private void SetModeShooting()
        {
            vm.Mode = ShootMode.Shooting;
            vm.IsFilterPanelVisible = false;
            vm.IsRetakeVisible = false;
            vm.IsNextVisible = false;
            vm.AreThumbnailsClickable = false;
            vm.IsCountdownVisible = true;
            vm.NotifyAll();
        }

        private void SetModeReview()
        {
            vm.Mode = ShootMode.Review;
            vm.IsFilterPanelVisible = false;
            vm.IsRetakeVisible = true;   // 先出現，但要看選中那張是否已重拍過
            vm.IsNextVisible = true;
            vm.AreThumbnailsClickable = true;
            vm.IsCountdownVisible = false;
            UpdateRetakeVisibilityBySelected();
            vm.NotifyAll();
        }

        private void SetModeFilter()
        {
            vm.Mode = ShootMode.Filter;
            vm.IsFilterPanelVisible = true;  // 左側濾鏡出現
            vm.IsRetakeVisible = false;      // 重拍消失
            vm.IsNextVisible = true;         // 仍可下一步
            vm.AreThumbnailsClickable = true; // 允許選照片套濾鏡
            vm.IsCountdownVisible = false;
            vm.NotifyAll();
        }

        private void LockUi(bool on)
        {
            _busy = on;
            vm.IsUiEnabled = !on;
            vm.NotifyAll();
        }


        private void UpdateRetakeVisibilityBySelected()
        {
            vm.IsRetakeVisible = (vm.Mode == ShootMode.Review) && !vm.Retaken[vm.SelectedIndex];
        }

        private async Task CountdownAndCaptureIntoIndex(int index)
        {
            LockUi(true);

            // #region agent log
            try
            {
                var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                File.AppendAllText(@"c:\Users\user\Documents\GitHub\photobooth-kiosk\.cursor\debug.log",
                    "{\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"H1\",\"location\":\"ShootPage.xaml.cs:CountdownAndCaptureIntoIndex\",\"message\":\"countdown_start\",\"data\":{\"index\":" + index + "},\"timestamp\":" + t + "}\n");
            }
            catch { }
            // #endregion

            // 大框改成「第index+1次拍攝的外框」
            ApplyBigViewForShootingFrame(index);

            // 方案 2：T=10s、3s、1s 各重對焦一次，每次 AF 前暫停拉流、AF 後等鏡頭，拍前最後一次在 T=1s，接著立刻拍
            vm.IsCountdownVisible = true;
            PlayCountdownSound();

            await Task.Delay(2000);
            for (int sec = 10; sec >= 1; sec--)
            {
                vm.CountdownText = sec.ToString();
                vm.NotifyAll();
                if (sec == 10 || sec == 3 || sec == 1)
                {
                    try { await _cameraService.TriggerEvfAfWithPauseAsync().ConfigureAwait(false); } catch { }
                }
                await Task.Delay(1000);
            }

            vm.CountdownText = "📸";
            vm.NotifyAll();

            // 拍照（EDSDK）：半按鎖焦 → 延遲 → 快門
            var file = await CapturePhotoAsync(index, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}_{index + 1}");
            CameraCaptureStore.SetCapture(index, file);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // 讀進記憶體，避免檔案被鎖
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            bmp.Freeze(); // 讓跨執行緒更安全

            // 1) 中間那排縮圖立刻出現
            vm.ShotImageSources[index] = bmp;

            // 2) 大紅框中間也立刻顯示剛拍的這張（更有「立刻出現」的感覺）
            vm.BigContentSource = bmp;

            // 3) 讓畫面有時間更新（不然你馬上進下一輪倒數，肉眼會覺得沒出現）
            vm.IsCountdownVisible = false;
            vm.NotifyAll();
            await Task.Delay(450);  // 你可調 200~800ms，看你想停留多久


            _shotCount = Math.Max(_shotCount, index + 1);

            // 拍攝中大畫面內容仍顯示「鏡頭預覽」，這裡先不動（你接真相機時更新 vm.BigContentSource）
            vm.IsCountdownVisible = false;
            vm.NotifyAll();

            LockUi(false);
        }

        private void ApplyBigViewForShootingFrame(int index)
        {
            vm.BigFrameSource = TryLoadImg(BigFrameFiles[Math.Clamp(index, 0, 3)]);
            // BigContentSource：拍攝中應該是鏡頭畫面（你接相機後在此持續更新）
            vm.BigContentSource ??= null;
            vm.NotifyAll();
        }

        private void ApplyBigViewForSelectedPhoto()
        {
            // 大框：依「選中照片」使用對應的框（你也可改成固定某個框）
            vm.BigFrameSource = TryLoadImg(BigFrameFiles[Math.Clamp(vm.SelectedIndex, 0, 3)]);
            vm.BigContentSource = vm.ShotImageSources[vm.SelectedIndex];
            vm.NotifyAll();
        }

        private static BitmapImage? TryLoadImg(string relativePath)
        {
            try
            {
                return new BitmapImage(new Uri($"pack://application:,,,/{relativePath}"));
            }
            catch
            {
                return null;
            }
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


        private void SelectThumb(int idx)
        {
            if (_busy) return;
            vm.SelectedIndex = idx;
            for (int i = 0; i < 4; i++) vm.IsThumbSelected[i] = (i == idx);

            ApplyBigViewForSelectedPhoto();
            UpdateRetakeVisibilityBySelected();
            vm.NotifyAll();
            if (vm.Mode == ShootMode.Filter) SyncFilterButtonsForSelectedPhoto();
        }

        // 縮圖點選
        private void PickThumb0(object sender, RoutedEventArgs e) => SelectThumb(0);
        private void PickThumb1(object sender, RoutedEventArgs e) => SelectThumb(1);
        private void PickThumb2(object sender, RoutedEventArgs e) => SelectThumb(2);
        private void PickThumb3(object sender, RoutedEventArgs e) => SelectThumb(3);

        // 重拍：點下去立即倒數→拍→替換該張；拍攝期間所有按鈕無效；每張最多一次，重拍過就隱藏重拍鈕
        private async void OnRetake(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            int idx = vm.SelectedIndex;
            if (vm.Retaken[idx]) return;

            // 保持目前畫面（Review/Filter 的布局不變）
            LockUi(true);

            vm.IsCountdownVisible = true;
            PlayCountdownSound();
            await Task.Delay(2000);
            for (int sec = 10; sec >= 1; sec--)
            {
                vm.CountdownText = sec.ToString();
                vm.NotifyAll();
                if (sec == 10 || sec == 3 || sec == 1)
                {
                    try { await _cameraService.TriggerEvfAfWithPauseAsync().ConfigureAwait(false); } catch { }
                }
                await Task.Delay(1000);
            }
            vm.CountdownText = "📸";
            vm.NotifyAll();

            // 拍照（EDSDK）
            var file = await CapturePhotoAsync(idx, $"retake_{DateTime.Now:yyyyMMdd_HHmmss}_{idx + 1}");
            CameraCaptureStore.SetCapture(idx, file);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            bmp.Freeze();

            // 直接替換中間縮圖那張
            vm.ShotImageSources[idx] = bmp;

            // 大畫面也立即更新成新拍的那張
            vm.BigContentSource = bmp;

            // 標記已重拍過一次 → 之後該張選到時重拍按鈕要消失
            vm.Retaken[idx] = true;

            vm.IsCountdownVisible = false;

            // 重拍後回到你要的第二張圖狀態（畫面不變，只解鎖）
            UpdateRetakeVisibilityBySelected(); // 你原本就有
            vm.NotifyAll();
            LockUi(false);
        }


        // 下一步：第一次按下 → 進入 Filter 模式（顯示左濾鏡、隱藏重拍）
        // Filter 模式再按下一步 → 合成/列印/上傳 + 顯示 QRCode（下一步我幫你接）
        private async void OnNext(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            if (vm.Mode == ShootMode.Review)
            {
                SetModeFilter();
                return;
            }

            if (vm.Mode == ShootMode.Filter)
            {
                // #region agent log
                try { var lp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log"); System.IO.File.AppendAllText(lp, System.Text.Json.JsonSerializer.Serialize(new { location = "ShootPage.OnNext:before_invoke", message = "Filter mode, calling ReturnToWebAndStartSynthesisRequested", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H1" }) + "\n"); } catch { }
                // #endregion
                // 回到 WebView，由 Vue 以 load_captures 取四張照片、合成、save_image/upload_file、顯示 QR
                PhotoBoothWin.Bridge.BoothBridge.ReturnToWebAndStartSynthesisRequested?.Invoke();
                // #region agent log
                try { var lp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub", "photobooth-kiosk", ".cursor", "debug.log"); System.IO.File.AppendAllText(lp, System.Text.Json.JsonSerializer.Serialize(new { location = "ShootPage.OnNext:after_invoke", message = "ReturnToWebAndStartSynthesisRequested returned", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), hypothesisId = "H1" }) + "\n"); } catch { }
                // #endregion
            }
        }

        // 濾鏡按鈕：你說「點一下選中出紅框，再點一次取消」
        // 這裡先把「濾鏡選擇」記到 vm.PhotoFilters[SelectedIndex]，真正套用（縮圖預覽/合成）下一步再接 SkiaSharp
        private void OnFilterClick(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (vm.Mode != ShootMode.Filter) return;

            if (sender is not Button btn) return;
            var id = btn.Tag?.ToString() ?? "";

            var cur = vm.PhotoFilters[vm.SelectedIndex] ?? "";
            vm.PhotoFilters[vm.SelectedIndex] = (cur == id) ? "" : id;

            // TODO：下一步把按鈕紅框狀態做成可視化（我會改成 ToggleButton + 綁定）
            MessageBox.Show($"第 {vm.SelectedIndex + 1} 張濾鏡 = {(vm.PhotoFilters[vm.SelectedIndex] == "" ? "無" : vm.PhotoFilters[vm.SelectedIndex])}");
        }

        private void ClearAllFilterChecks()
        {
            T_Slim.IsChecked = false;
            T_Eye.IsChecked = false;
            T_Bright.IsChecked = false;
            T_Retro.IsChecked = false;
        }

        private void SyncFilterButtonsForSelectedPhoto()
        {
            // 依照目前選中的照片，把左邊按鈕勾選狀態同步回來
            var id = vm.PhotoFilters[vm.SelectedIndex] ?? "";
            ClearAllFilterChecks();

            if (id == "slim") T_Slim.IsChecked = true;
            else if (id == "eye") T_Eye.IsChecked = true;
            else if (id == "bright") T_Bright.IsChecked = true;
            else if (id == "retro") T_Retro.IsChecked = true;
        }

        private void OnFilterToggleClick(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (vm.Mode != ShootMode.Filter) return;

            if (sender is not System.Windows.Controls.Primitives.ToggleButton tb) return;
            var clicked = tb.Tag?.ToString() ?? "";

            var cur = vm.PhotoFilters[vm.SelectedIndex] ?? "";

            // 規則：再點一次取消
            if (cur == clicked)
            {
                vm.PhotoFilters[vm.SelectedIndex] = "";
                ClearAllFilterChecks(); // 全部取消 → 橘框消失
                return;
            }

            // 選新濾鏡：只允許一個亮
            vm.PhotoFilters[vm.SelectedIndex] = clicked;
            ClearAllFilterChecks();
            tb.IsChecked = true; // 讓被點的那個出現橘框
        }

        private async Task InitializeCameraAsync()
        {
            try
            {
                if (!_liveViewHooked)
                {
                    _cameraService.LiveViewFrameReady += OnLiveViewFrameReady;
                    _liveViewHooked = true;
                }
                await _cameraService.InitializeAsync();
                await _cameraService.StartLiveViewAsync();
            }
            catch
            {
                // 初始化失敗時，仍可用假相機拍攝（避免流程卡死）
            }
        }

        private void OnLiveViewFrameReady(object? sender, BitmapSource frame)
        {
            Dispatcher.Invoke(() =>
            {
                if (vm.Mode == ShootMode.Shooting)
                {
                    vm.BigContentSource = frame;
                    vm.NotifyAll();
                }
            });
        }

        private async Task<string> CapturePhotoAsync(int index, string prefix)
        {
            try
            {
                // 半按快門觸發對焦，再放開後拍照
                await _cameraService.HalfPressShutterAsync();
                await Task.Delay(300).ConfigureAwait(false);
                return await _cameraService.TakePictureAsync(_dir, index);
            }
            catch
            {
                // 備援：EDSDK 拍照失敗時改用假相機，避免流程卡死
                var file = System.IO.Path.Combine(_dir, $"{prefix}.png");
                FakeCamera.SaveFakeShot(file, index);
                return file;
            }
        }

    }
}

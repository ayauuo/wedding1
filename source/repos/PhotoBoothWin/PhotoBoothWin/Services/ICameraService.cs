using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PhotoBoothWin.Services
{
    public interface ICameraService : IDisposable
    {
        event EventHandler<BitmapSource>? LiveViewFrameReady;
        event EventHandler<string>? PhotoCaptured;

        bool IsConnected { get; }

        Task InitializeAsync();
        Task StartLiveViewAsync();
        Task StopLiveViewAsync();
        /// <summary>半按快門觸發對焦再放開。</summary>
        Task HalfPressShutterAsync();
        /// <summary>開始持續半按快門（對焦鎖定），倒數期間呼叫；倒數結束後須呼叫 EndHalfPressAsync 放開。</summary>
        Task StartHalfPressAsync();
        /// <summary>結束持續半按快門（放開快門鈕）。</summary>
        Task EndHalfPressAsync();
        /// <summary>觸發一次 EVF 自動對焦（DoEvfAf Off→On）。</summary>
        Task TriggerEvfAfAsync();
        /// <summary>暫停 LiveView 拉流後觸發 AF、再等鏡頭完成，避免 Busy；建議在 T=10/3/1 秒各呼叫一次。</summary>
        Task TriggerEvfAfWithPauseAsync();
        Task AutoFocusAsync();

        /// <summary>目前從「遠端歸零」後往近端驅動的步數（僅 Near1 成功時 +1）。</summary>
        int EvfDriveFocusStep { get; }

        /// <summary>往近端最多允許幾步 Near1；超過則 <see cref="TryDriveEvfFocusNear1Async"/> 不送指令。</summary>
        int EvfDriveFocusMaxNearSteps { get; set; }

        /// <summary>送一次 DriveLensEvf Near1；成功則步數 +1，且受 <see cref="EvfDriveFocusMaxNearSteps"/> 限制。需 Live View 開啟。</summary>
        Task<bool> TryDriveEvfFocusNear1Async();

        /// <summary>送一次 DriveLensEvf Far1；成功則步數 -1。需 Live View 開啟。</summary>
        Task<bool> TryDriveEvfFocusFar1Async();

        /// <summary>連續送 Far3 將焦點往遠端頂（依機鏡可能近似無限遠），並將步數歸零。需 Live View 開啟。</summary>
        Task CalibrateEvfFocusFarEndAsync(int far3RepeatCount = 24);

        Task<string> TakePictureAsync(string outputDir, int index, bool restartLiveViewAfter = true);
    }
}

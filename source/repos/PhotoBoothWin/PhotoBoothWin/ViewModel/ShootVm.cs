using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Collections.ObjectModel;

namespace PhotoBoothWin.ViewModel
{
    public enum ShootMode { Shooting, Review, Filter, Processing, Qr }

    public class ShootVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void On([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // ===== 狀態 =====
        public ShootMode Mode { get; set; } = ShootMode.Shooting;

        public bool IsFilterPanelVisible { get; set; } = false;
        public bool IsRetakeVisible { get; set; } = false;
        public bool IsNextVisible { get; set; } = false;

        // UI 鎖定：重拍/拍攝倒數時會 false
        public bool IsUiEnabled { get; set; } = true;

        // 模式允許縮圖點選（Review/Filter 才是 true）
        public bool AreThumbnailsClickable { get; set; } = false;

        // 真正用在 XAML：要同時符合「沒鎖」+「模式允許」
        public bool CanClickThumbs => IsUiEnabled && AreThumbnailsClickable;

        public bool IsCountdownVisible { get; set; } = false;
        public string CountdownText { get; set; } = "10";

        // ===== 影像 =====
        public ObservableCollection<ImageSource?> ShotImageSources { get; } =
            new ObservableCollection<ImageSource?>(new ImageSource?[4]);

        public ObservableCollection<ImageSource?> ThumbFrameSources { get; } =
            new ObservableCollection<ImageSource?>(new ImageSource?[4]);

        public ImageSource? BigFrameSource { get; set; }
        public ImageSource? BigContentSource { get; set; }

        public bool[] IsThumbSelected { get; } = new bool[4];
        public int SelectedIndex { get; set; } = 0;

        public bool[] Retaken { get; } = new bool[4];
        public string[] PhotoFilters { get; } = new string[4];

        public void NotifyAll()
        {
            On(nameof(Mode));
            On(nameof(IsFilterPanelVisible));
            On(nameof(IsRetakeVisible));
            On(nameof(IsNextVisible));
            On(nameof(IsUiEnabled));
            On(nameof(AreThumbnailsClickable));
            On(nameof(CanClickThumbs));
            On(nameof(IsCountdownVisible));
            On(nameof(CountdownText));
            On(nameof(ShotImageSources));
            On(nameof(ThumbFrameSources));
            On(nameof(BigFrameSource));
            On(nameof(BigContentSource));
            On(nameof(IsThumbSelected));
            On(nameof(SelectedIndex));
        }
    }
}

using System;
using System;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using PhotoBoothWin.Bridge;
using PhotoBoothWin.Models;

namespace PhotoBoothWin.Pages
{
    public partial class PreviewPage : Page
    {
        public PreviewPage()
        {
            InitializeComponent();
            Loaded += (_, __) => RefreshAll();
        }

        private BoothSession S => BoothStore.Current;

        private void RefreshAll()
        {
            Img1.Source = Load(S.ShotPaths[0]);
            Img2.Source = Load(S.ShotPaths[1]);
            Img3.Source = Load(S.ShotPaths[2]);
            Img4.Source = Load(S.ShotPaths[3]);

            BtnRetake1.IsEnabled = !S.Retaken[0];
            BtnRetake2.IsEnabled = !S.Retaken[1];
            BtnRetake3.IsEnabled = !S.Retaken[2];
            BtnRetake4.IsEnabled = !S.Retaken[3];

            if (S.Retaken[0]) BtnRetake1.Content = "已重拍（不可再重拍）";
            if (S.Retaken[1]) BtnRetake2.Content = "已重拍（不可再重拍）";
            if (S.Retaken[2]) BtnRetake3.Content = "已重拍（不可再重拍）";
            if (S.Retaken[3]) BtnRetake4.Content = "已重拍（不可再重拍）";

            TxtFilter1.Text = $"濾鏡：{NameOf(S.PhotoFilters[0])}";
            TxtFilter2.Text = $"濾鏡：{NameOf(S.PhotoFilters[1])}";
            TxtFilter3.Text = $"濾鏡：{NameOf(S.PhotoFilters[2])}";
            TxtFilter4.Text = $"濾鏡：{NameOf(S.PhotoFilters[3])}";

            RefreshActiveUI();
        }

        private void RefreshActiveUI()
        {
            // 讓被選中的照片紅框
            PhotoBd1.BorderBrush = (S.ActiveIndex == 0) ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.White;
            PhotoBd2.BorderBrush = (S.ActiveIndex == 1) ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.White;
            PhotoBd3.BorderBrush = (S.ActiveIndex == 2) ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.White;
            PhotoBd4.BorderBrush = (S.ActiveIndex == 3) ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.White;

            ActiveHint.Text = $"目前選擇：第 {S.ActiveIndex + 1} 張";

            // 依照「這一張」已選濾鏡，更新右側按鈕紅框
            var cur = S.PhotoFilters[S.ActiveIndex] ?? "";
            SetFilterButtonChecked(cur);
        }

        private void SetFilterButtonChecked(string filterId)
        {
            // 先全部取消
            F_None.IsChecked = false;
            F_BW.IsChecked = false;
            F_Warm.IsChecked = false;
            F_Cool.IsChecked = false;
            F_Vivid.IsChecked = false;

            // 再把目前這張的濾鏡設為選中
            if (filterId == "") F_None.IsChecked = true;
            else if (filterId == "bw") F_BW.IsChecked = true;
            else if (filterId == "warm") F_Warm.IsChecked = true;
            else if (filterId == "cool") F_Cool.IsChecked = true;
            else if (filterId == "vivid") F_Vivid.IsChecked = true;
        }

        private static BitmapImage? Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return new BitmapImage(new Uri(path));
        }

        private static string NameOf(string id) => id switch
        {
            "" => "原圖",
            "bw" => "黑白",
            "warm" => "暖色",
            "cool" => "冷色",
            "vivid" => "鮮豔",
            _ => id
        };

        // --- 點照片選擇 ---
        private void Pick1(object sender, System.Windows.Input.MouseButtonEventArgs e) { S.ActiveIndex = 0; RefreshActiveUI(); }
        private void Pick2(object sender, System.Windows.Input.MouseButtonEventArgs e) { S.ActiveIndex = 1; RefreshActiveUI(); }
        private void Pick3(object sender, System.Windows.Input.MouseButtonEventArgs e) { S.ActiveIndex = 2; RefreshActiveUI(); }
        private void Pick4(object sender, System.Windows.Input.MouseButtonEventArgs e) { S.ActiveIndex = 3; RefreshActiveUI(); }

        // --- 點濾鏡：同一個再點一次＝取消 ---
        private void OnFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb) return;
            var clicked = (tb.Tag?.ToString()) ?? "";

            var cur = S.PhotoFilters[S.ActiveIndex] ?? "";

            // 規則：再點一次取消 → 回到原圖("")
            if (clicked == cur)
            {
                S.PhotoFilters[S.ActiveIndex] = "";
            }
            else
            {
                S.PhotoFilters[S.ActiveIndex] = clicked;
            }

            // 更新顯示文字
            TxtFilter1.Text = $"濾鏡：{NameOf(S.PhotoFilters[0])}";
            TxtFilter2.Text = $"濾鏡：{NameOf(S.PhotoFilters[1])}";
            TxtFilter3.Text = $"濾鏡：{NameOf(S.PhotoFilters[2])}";
            TxtFilter4.Text = $"濾鏡：{NameOf(S.PhotoFilters[3])}";

            RefreshActiveUI();
        }

        // --- 重拍（每張最多一次） ---
        private void GoRetake(int index)
        {
            if (S.Retaken[index]) return;
            S.ActiveIndex = index; // 重拍完回來仍選中這張
            NavigationService?.Navigate(new RetakePage(index));
        }

        private void Retake1(object sender, RoutedEventArgs e) => GoRetake(0);
        private void Retake2(object sender, RoutedEventArgs e) => GoRetake(1);
        private void Retake3(object sender, RoutedEventArgs e) => GoRetake(2);
        private void Retake4(object sender, RoutedEventArgs e) => GoRetake(3);

        private void BackToIdle(object sender, RoutedEventArgs e)
        {
            BoothStore.Reset();
            if (BoothBridge.IsWpfShootEmbedded)
                BoothBridge.ReturnToWebViewRequested?.Invoke();
            else
                NavigationService?.Navigate(new IdlePage());
        }

        // 先留著：下一步我們會做 ComposePage（SkiaSharp 合成 + 套每張濾鏡）
        private void GoCompose(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("下一步：合成（下一則我幫你接 SkiaSharp 真正套濾鏡+合成輸出）");
        }
    }
}




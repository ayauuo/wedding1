using System;
using System.Collections.Generic;
using System.Text;

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoBoothWin
{
    /// <summary>
    /// 假相機：產生純色方塊圖當作照片。保留為備援：當 EDSDK 相機未連線或 TakePictureAsync 失敗時，
    /// ShootPage.CapturePhotoAsync 會 fallback 呼叫本方法，避免流程卡死。其餘頁面（CapturePage、RetakePage）
    /// 若尚未接 EDSDK 亦可沿用。
    /// </summary>
    public static class FakeCamera
    {
        /// <summary>產生假照片（純色方塊），供 EDSDK 失敗時備援使用。</summary>
        public static void SaveFakeShot(string path, int i)
        {
            int w = 800, h = 600;
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);

            uint color = i switch
            {
                0 => 0xFF_00_00_FF, // 紅
                1 => 0xFF_00_FF_00, // 綠
                2 => 0xFF_FF_00_00, // 藍
                _ => 0xFF_FF_FF_00  // 黃
            };

            int stride = w * 4;
            byte[] pixels = new byte[h * stride];
            for (int p = 0; p < pixels.Length; p += 4)
            {
                pixels[p + 0] = (byte)(color & 0xFF);         // B
                pixels[p + 1] = (byte)((color >> 8) & 0xFF);  // G
                pixels[p + 2] = (byte)((color >> 16) & 0xFF); // R
                pixels[p + 3] = (byte)((color >> 24) & 0xFF); // A
            }
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), pixels, stride, 0);

            using var fs = new FileStream(path, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(wb));
            encoder.Save(fs);
        }
    }
}

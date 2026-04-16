using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace PhotoBoothWin.Services
{
    public static class TestImageMaker
    {
        public static void Make(string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            using var bmp = new Bitmap(1200, 1800); // 4x6 @ 300dpi 常見像素
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            using var font = new Font("Arial", 64, FontStyle.Bold);
            g.DrawString("TEST PRINT", font, Brushes.Black, 120, 120);
            g.DrawString(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), new Font("Arial", 36), Brushes.DarkBlue, 120, 260);

            bmp.Save(filePath, ImageFormat.Jpeg);
        }
    }
}

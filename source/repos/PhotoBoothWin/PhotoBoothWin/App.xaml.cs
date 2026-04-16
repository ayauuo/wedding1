using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace PhotoBoothWin
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 使用內建的 RS232-ICT004 驅動程式（不需要外部程式）
            System.Diagnostics.Debug.WriteLine("✓ 使用內建的 RS232-ICT004 驅動程式");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }

}

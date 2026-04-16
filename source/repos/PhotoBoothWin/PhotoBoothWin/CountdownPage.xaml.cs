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
using System.Threading.Tasks;

namespace PhotoBoothWin.Pages
{
    public partial class CountdownPage : Page
    {
        private readonly string _templateId;

        public CountdownPage(string templateId)
        {
            InitializeComponent();
            _templateId = templateId;
            TemplateText.Text = $"版型：{_templateId}";
            Loaded += async (_, __) => await RunCountdown();
        }

        private async Task RunCountdown()
        {
            for (int i = 10; i >= 1; i--)
            {
                CountdownText.Text = i.ToString();
                await Task.Delay(1000);
            }
            NavigationService?.Navigate(new CapturePage());
        }
    }
}


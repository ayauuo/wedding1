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
using PhotoBoothWin.Bridge;

namespace PhotoBoothWin.Pages
{
    public partial class TemplatePage : Page
    {
        public TemplatePage()
        {
            InitializeComponent();
        }

        private void GoShoot(string templateId)
        {
            BoothStore.Reset();
            BoothStore.Current.TemplateId = templateId;
            NavigationService?.Navigate(new ShootPage());
        }

        private void OnTemplateA(object sender, RoutedEventArgs e) => GoShoot("A");
        private void OnTemplateB(object sender, RoutedEventArgs e) => GoShoot("B");
        private void OnTemplateC(object sender, RoutedEventArgs e) => GoShoot("C");


        private void OnBack(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null && NavigationService.CanGoBack)
                NavigationService.GoBack();
            else if (BoothBridge.IsWpfShootEmbedded)
                BoothBridge.ReturnToWebViewRequested?.Invoke();
        }
    }
}


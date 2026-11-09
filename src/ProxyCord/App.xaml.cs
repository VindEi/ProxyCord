using System.Windows;

namespace ProxyCord
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            DispatcherUnhandledException += (s, e) =>
            {
                System.Windows.MessageBox.Show(
                    $"ProxyCord encountered an error:\n\n{e.Exception.Message}",
                    "ProxyCord Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            };
        }
    }
}

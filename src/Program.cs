using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

[assembly: AssemblyTitle("PomoNoBro")]
[assembly: AssemblyProduct("PomoNoBro")]
[assembly: AssemblyVersion("0.1.0.0")]

namespace PomoNoBro
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (var mutex = new Mutex(true, @"Local\PomoNoBro.Offline", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("PomoNoBro is already open.", "PomoNoBro");
                    return;
                }

                try
                {
                    var app = new Application();
                    var window = CreateWindow();
                    var directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PomoNoBro");
                    new DesktopApp(window, directory, AppDomain.CurrentDomain.BaseDirectory);
                    app.Run(window);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.GetBaseException().Message, "PomoNoBro could not start",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        internal static Window CreateWindow()
        {
            using (var xaml = typeof(Program).Assembly.GetManifestResourceStream("PomoNoBro.MainWindow.xaml"))
                return (Window)XamlReader.Load(xaml);
        }
    }
}

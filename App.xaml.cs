using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ModernPlayer.Services;

namespace ModernPlayer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Global Exception Handlers to guarantee 100% crash-proof execution
            AppDomain.CurrentDomain.UnhandledException += (s, args) => { };
            DispatcherUnhandledException += (s, args) => { args.Handled = true; };
            TaskScheduler.UnobservedTaskException += (s, args) => { args.SetObserved(); };

            base.OnStartup(e);

            // Single Instance check: if already running, send file to it and exit
            if (!SingleInstanceManager.IsFirstInstance())
            {
                if (e.Args != null && e.Args.Length > 0)
                {
                    SingleInstanceManager.SendToExistingInstance(e.Args[0]);
                }
                Shutdown(0);
                return;
            }

            // Start background pipe server to listen for new files opened by user
            SingleInstanceManager.StartServer();

            // Register in HKCU so Windows "Open with..." recommends this player
            try
            {
                FileAssociationService.RegisterOpenWith();
            }
            catch
            {
            }

            var mainWindow = new MainWindow(e.Args);
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SingleInstanceManager.Cleanup();
            base.OnExit(e);
        }
    }
}

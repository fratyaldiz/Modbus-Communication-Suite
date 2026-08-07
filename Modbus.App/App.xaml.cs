using System;
using System.Windows;

using Modbus.App.Services;
using Modbus.App.Views;

namespace Modbus.App
{
    /// <summary>
    /// Uygulama giriş noktası. Her açılışta önce Role Selection ekranını gösterir ve
    /// kullanıcının seçimine göre Master veya Slave çalışma penceresini açar.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Uygulama genelinde paylaşılan ayar servisi.</summary>
        public static SettingsService Settings { get; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Role ekranı ile çalışma penceresi arası geçişte uygulama kapanmasın.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Settings.Load();

            var roleWindow = new RoleSelectionWindow();
            bool? ok = roleWindow.ShowDialog();

            if (ok != true || roleWindow.SelectedRole == null)
            {
                Shutdown();
                return;
            }

            string role = roleWindow.SelectedRole;

            Window working;
            try
            {
                working = role == "Master" ? new MasterWindow() : (Window)new SlaveWindow();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Çalışma penceresi açılırken hata:\n\n" + ex.Message,
                    "Modbus Communication Suite",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            Settings.Current.LastRole = role;
            Settings.Save();

            MainWindow = working;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            working.Show();
        }
    }
}

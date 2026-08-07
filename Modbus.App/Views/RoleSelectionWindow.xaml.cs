using System.Windows;

namespace Modbus.App.Views
{
    /// <summary>
    /// Açılış ekranı. Kullanıcı Master mı Slave mi çalışacağını seçer.
    /// Seçim SelectedRole'da döner; App bu sonuca göre ilgili çalışma penceresini açar.
    /// </summary>
    public partial class RoleSelectionWindow : Window
    {
        public string? SelectedRole { get; private set; }

        public RoleSelectionWindow()
        {
            InitializeComponent();
        }

        private void OnMasterClick(object sender, RoutedEventArgs e)
        {
            SelectedRole = "Master";
            DialogResult = true;
        }

        private void OnSlaveClick(object sender, RoutedEventArgs e)
        {
            SelectedRole = "Slave";
            DialogResult = true;
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            SelectedRole = null;
            DialogResult = false;
        }
    }
}

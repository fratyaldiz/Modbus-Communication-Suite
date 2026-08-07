using System.Windows;

using Modbus.App.Models;
using Modbus.App.ViewModels;

namespace Modbus.App.Views
{
    /// <summary>Register ekleme/düzenleme diyaloğu. Sonuç Result'ta döner.</summary>
    public partial class AddEditRegisterWindow : Window
    {
        private readonly AddEditRegisterViewModel _vm;

        public DeviceRegisterDefinition? Result { get; private set; }

        public AddEditRegisterWindow(AddEditRegisterViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (!_vm.TryBuild(out DeviceRegisterDefinition def, out string error))
            {
                MessageBox.Show(this, error, "Geçersiz değer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = def;
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

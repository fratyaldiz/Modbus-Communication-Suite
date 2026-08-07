using System.Linq;
using System.Windows;

using Modbus.App.Models;
using Modbus.App.ViewModels;

namespace Modbus.App.Views
{
    /// <summary>
    /// SLAVE / SERVER çalışma penceresi. Add/Edit/Delete diyalogları burada açılır;
    /// asıl register mantığı RegisterMemoryService'te (tek doğruluk kaynağı).
    /// </summary>
    public partial class SlaveWindow : Window
    {
        public SlaveWindow()
        {
            InitializeComponent();
        }

        private SlaveViewModel Vm => (SlaveViewModel)DataContext;

        private int AddressBase => Vm.SelectedProfile?.AddressBase ?? 40000;

        private void OnAddRegister(object sender, RoutedEventArgs e)
        {
            int nextLogical = Vm.Memory.Registers.Count > 0
                ? Vm.Memory.Registers.Max(r => r.LogicalAddress) + 1
                : AddressBase + 160;

            var dialogVm = new AddEditRegisterViewModel(AddressBase)
            {
                PlcAddress = nextLogical.ToString()
            };

            var dlg = new AddEditRegisterWindow(dialogVm) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                if (!Vm.Memory.TryAdd(dlg.Result, out string error))
                    MessageBox.Show(this, error, "Eklenemedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnEditRegister(object sender, RoutedEventArgs e)
        {
            if (Vm.SelectedRegister is not DeviceRegisterDefinition target)
            {
                MessageBox.Show(this, "Önce düzenlenecek register'ı seçin.", "Seçim yok",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialogVm = new AddEditRegisterViewModel(AddressBase, target);
            var dlg = new AddEditRegisterWindow(dialogVm) { Owner = this };

            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                if (!Vm.Memory.TryUpdate(target, dlg.Result, out string error))
                    MessageBox.Show(this, error, "Güncellenemedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnDeleteRegister(object sender, RoutedEventArgs e)
        {
            if (Vm.SelectedRegister is not DeviceRegisterDefinition target)
            {
                MessageBox.Show(this, "Önce silinecek register'ı seçin.", "Seçim yok",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string warn = target.IsProfileRegister
                ? $"{target.LogicalAddress} {target.Name} bir PROFİL register'ıdır. Yine de silmek istiyor musunuz?"
                : $"{target.LogicalAddress} {target.Name} register'ını silmek istediğinize emin misiniz?";

            if (MessageBox.Show(this, warn, "Silme onayı", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Vm.Memory.Remove(target);
        }
    }
}

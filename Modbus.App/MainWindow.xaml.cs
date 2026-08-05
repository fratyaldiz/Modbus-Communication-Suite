using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Modbus.App.Models;

namespace Modbus.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Kullanıcı register tablosunda bir satıra sağ tıkladığında
        /// önce o satırı seçer.
        /// </summary>
        private void RegisterGrid_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not DataGrid dataGrid)
                return;

            DependencyObject? source =
                e.OriginalSource as DependencyObject;

            while (source != null &&
                   source is not DataGridRow)
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (source is not DataGridRow row)
                return;

            row.IsSelected = true;
            dataGrid.SelectedItem = row.Item;
            dataGrid.CurrentItem = row.Item;
        }

        /// <summary>
        /// Sağ tık menüsünden seçilen veri türünü registera uygular.
        /// </summary>
        private void SetDataType_Click(
            object sender,
            RoutedEventArgs e)
        {
            RegisterItem? register =
                GetRegisterFromMenuItem(sender);

            if (register == null ||
                sender is not MenuItem menuItem ||
                menuItem.Tag is not string dataType)
            {
                return;
            }

            register.DataType = dataType;
            register.RefreshDerived();
        }

        /// <summary>
        /// Sağ tık menüsünden seçilen byte sırasını registera uygular.
        /// </summary>
        private void SetByteOrder_Click(
            object sender,
            RoutedEventArgs e)
        {
            RegisterItem? register =
                GetRegisterFromMenuItem(sender);

            if (register == null ||
                sender is not MenuItem menuItem ||
                menuItem.Tag is not string byteOrder)
            {
                return;
            }

            register.ByteOrder = byteOrder;
            register.RefreshDerived();
        }

        /// <summary>
        /// Yorumlanmış Display Value değerini panoya kopyalar.
        /// </summary>
        private void CopyDisplayValue_Click(
            object sender,
            RoutedEventArgs e)
        {
            RegisterItem? register =
                GetRegisterFromMenuItem(sender);

            if (register == null)
                return;

            Clipboard.SetText(
                register.DisplayValue ?? string.Empty);
        }

        /// <summary>
        /// Registerın gerçek 16 bit ham değerini panoya kopyalar.
        /// </summary>
        private void CopyRawValue_Click(
            object sender,
            RoutedEventArgs e)
        {
            RegisterItem? register =
                GetRegisterFromMenuItem(sender);

            if (register == null)
                return;

            Clipboard.SetText(register.Value.ToString());
        }

        /// <summary>
        /// Yalnızca seçilen satırdaki değişim rengini temizler.
        /// </summary>
        private void ResetRowColor_Click(
            object sender,
            RoutedEventArgs e)
        {
            RegisterItem? register =
                GetRegisterFromMenuItem(sender);

            register?.ResetChangeState();
        }

        /// <summary>
        /// ContextMenu DataContext'i üzerinden seçili RegisterItem alınır.
        /// </summary>
        private static RegisterItem? GetRegisterFromMenuItem(
            object sender)
        {
            return sender is MenuItem menuItem
                ? menuItem.DataContext as RegisterItem
                : null;
        }
    }
}
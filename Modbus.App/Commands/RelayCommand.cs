using System;
using System.Windows.Input;

namespace Modbus.App.Commands
{
    /// <summary>
    /// RelayCommand: MVVM deseninde butonları koda bağlayan "komut" sınıfı.
    ///
    /// MVVM nedir? (Model - View - ViewModel)
    ///  - View       : Ekran (XAML). Görsel kısım.
    ///  - ViewModel  : Ekranın "beyni". Veriyi ve butonların ne yapacağını tutar.
    ///  - Model      : Ham veri (ModbusDevice gibi).
    /// Amaç: görsel (XAML) ile mantık (C#) birbirinden ayrı dursun.
    ///
    /// Peki buton nasıl kod çağırır? WPF butonları "Command" adında bir özelliğe
    /// bağlanır. Command, ICommand arayüzünü uygulayan bir nesne olmalıdır.
    /// İşte RelayCommand bunu sağlar: ona "çalışınca şu fonksiyonu çağır" deriz.
    ///
    /// ": ICommand" demek: WPF'in beklediği komut sözleşmesini uyguluyoruz.
    /// </summary>
    public class RelayCommand : ICommand
    {
        // Buton tıklanınca çalışacak asıl iş (fonksiyon).
        private readonly Action _execute;

        // Butonun tıklanabilir olup olmadığını belirleyen kontrol (isteğe bağlı).
        private readonly Func<bool>? _canExecute;

        /// <param name="execute">Komut çalışınca yapılacak iş.</param>
        /// <param name="canExecute">Butonun aktif olup olmayacağını söyleyen kontrol (opsiyonel).</param>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            // "??" : execute null ise hata fırlat. Komutun bir işi olmalı.
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// WPF bu metodu çağırarak "bu buton şu an tıklanabilir mi?" diye sorar.
        /// canExecute verilmemişse her zaman true (tıklanabilir) döneriz.
        /// </summary>
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        /// <summary>Buton tıklanınca WPF burayı çağırır; biz de asıl işi çalıştırırız.</summary>
        public void Execute(object? parameter) => _execute();

        /// <summary>
        /// "Tıklanabilirlik durumu değişti" olayı. WPF bunu dinler ve gerektiğinde
        /// CanExecute'u yeniden sorar. CommandManager'a bağlamak standart yöntemdir.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}

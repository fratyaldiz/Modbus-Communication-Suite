using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Modbus.App.ViewModels
{
    /// <summary>
    /// Tüm ViewModel'lerin ortak atası.
    ///
    /// Görevi: Bir özellik (property) değiştiğinde EKRANA "ben değiştim, kendini
    /// güncelle" diye haber vermek. Bunu "INotifyPropertyChanged" arayüzü sağlar.
    ///
    /// Neden lazım? WPF binding'i (bağlama) sihirli değildir. Bir C# özelliğini
    /// değiştirdiğinde ekran kendiliğinden yenilenmez. Bu arayüz sayesinde
    /// "PropertyChanged" olayını tetikleyince WPF ekranı tazeler.
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        // WPF'in dinlediği olay. Bir özellik değişince tetiklenir.
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Belirtilen özelliğin değiştiğini WPF'e bildirir.
        /// [CallerMemberName]: bu metodu bir property'nin set'inde çağırırsak,
        /// derleyici property adını OTOMATİK doldurur; elle "IpAddress" yazmamıza gerek kalmaz.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

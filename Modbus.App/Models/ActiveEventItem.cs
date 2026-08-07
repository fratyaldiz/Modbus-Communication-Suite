using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Modbus.App.Models
{
    /// <summary>
    /// Status / Active Events tablosundaki tek satır: bir bitin anlık durumu.
    /// </summary>
    public sealed class ActiveEventItem : INotifyPropertyChanged
    {
        public int Bit { get; init; }
        public string Severity { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        private bool _active;
        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value)
                    return;

                _active = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateText));
            }
        }

        public string StateText => Active ? "ACTIVE" : "INACTIVE";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
